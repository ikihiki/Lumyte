using Lumyte.Memory;

namespace Lumyte.Resources;

/// <content>Coordinates generation cleanup, cache reclamation and manager shutdown.</content>
public sealed partial class ResourceManager
{
    /// <summary>Releases unused generations and retries retained cleanup failures without invalidating live ownership.</summary>
    /// <param name="cancellationToken">Stops before starting the next cleanup operation.</param>
    /// <returns>The number of completely released resource generations.</returns>
    public async ValueTask<int> TrimAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnsureRunning();
            _operations++;
        }

        try
        {
            await _maintenance.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await TrimCoreAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _maintenance.Release();
            }
        }
        finally
        {
            lock (_gate)
            {
                _operations--;
            }
        }
    }

    /// <summary>Trims unused resources and observes actual committed bytes released from the shared pool ledger.</summary>
    /// <param name="pool">The pool whose release counter is observed.</param>
    /// <param name="targetBytes">The requested positive byte count; whole-generation reclamation may exceed it.</param>
    /// <param name="cancellationToken">Stops before starting the next cleanup operation.</param>
    /// <returns>The cumulative pool releases observed during reclamation, including concurrent returns.</returns>
    public async ValueTask<ulong> ReclaimAsync(MemoryPoolId pool, ulong targetBytes, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfZero(targetBytes);
        ulong before = _memory.GetSnapshot(pool).ReleasedBytes;
        await TrimAsync(cancellationToken).ConfigureAwait(false);
        return unchecked(_memory.GetSnapshot(pool).ReleasedBytes - before);
    }

    /// <summary>Ends the manager after external owners and operations finish, retaining failed cleanup for a later retry.</summary>
    /// <returns>The shared shutdown attempt.</returns>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            if (_disposeTask is { IsCompleted: false } pending)
            {
                return new(pending);
            }

            if (_externalOwners != 0 || _operations != 0 || _loads.Count != 0)
            {
                throw new InvalidOperationException("Return resource references, leases and resource snapshots, and complete all operations before disposing the manager.");
            }

            _closing = true;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        _ = DisposeCoreAsync(completion);
        return new(completion.Task);
    }

    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            _reclaimerRegistration.Dispose();
            UnsubscribeRuntimes();
            await _maintenance.WaitAsync().ConfigureAwait(false);
            try
            {
                await TrimCoreAsync(CancellationToken.None).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_generations.Count != 0)
                    {
                        throw new InvalidOperationException("Resource generations still retain internal ownership after shutdown cleanup.");
                    }

                    _metadata.Clear();
                    _invalidated.Clear();
                    _disposed = true;
                }
            }
            finally
            {
                _maintenance.Release();
            }

            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private async ValueTask<int> TrimCoreAsync(CancellationToken cancellationToken)
    {
        List<Exception>? errors = null;
        int released = 0;
        try
        {
            released = await ReleaseUnusedAsync(null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            (errors ??= []).Add(error);
        }

        lock (_gate)
        {
            PruneMetadata();
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more resource cleanup operations failed.", errors);
        }

        return released;
    }

    private async ValueTask<int> ReleaseUnusedAsync(HashSet<ResourceGeneration>? candidates, CancellationToken cancellationToken)
    {
        HashSet<ResourceGeneration> attempted = [];
        List<Exception>? errors = null;
        int released = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResourceGeneration? candidate;
            lock (_gate)
            {
                candidate = _generations.FirstOrDefault(generation => (generation.State is ResourceGenerationState.Ready or ResourceGenerationState.CleanupFailed)
                    && generation.Pins == 0
                    && !attempted.Contains(generation)
                    && (candidates is null || (generation.State == ResourceGenerationState.Ready && candidates.Contains(generation) && !ReferenceEquals(_published.GetValueOrDefault(generation.Node), generation))));
                if (candidate is null)
                {
                    break;
                }

                if (ReferenceEquals(_published.GetValueOrDefault(candidate.Node), candidate))
                {
                    Dictionary<ResourceNode, ResourceGeneration> updated = new(_published);
                    updated.Remove(candidate.Node);
                    SetPublished(updated);
                }

                candidate.State = candidate.State == ResourceGenerationState.CleanupFailed
                    ? ResourceGenerationState.RetryingCleanup
                    : ResourceGenerationState.Releasing;
                attempted.Add(candidate);
            }

            try
            {
                await CleanupGenerationAsync(candidate, false).ConfigureAwait(false);
                released++;
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more resource generations could not be released.", errors);
        }

        return released;
    }

    private async ValueTask CleanupGenerationAsync(ResourceGeneration generation, bool buildOnly)
    {
        if (!buildOnly)
        {
            lock (_gate)
            {
                if (generation.State == ResourceGenerationState.Building)
                {
                    generation.State = ResourceGenerationState.Releasing;
                }
            }
        }

        try
        {
            if (!buildOnly && generation.CleanupPhase == ResourceGenerationCleanupPhase.Output)
            {
                if (generation.Value is { } output)
                {
                    await generation.Runtime.ReleaseAsync(generation.Stage, output).ConfigureAwait(false);
                }

                generation.CleanupPhase = ResourceGenerationCleanupPhase.Resources;
            }

            for (int index = generation.Cleanups.Count - 1; index >= 0; index--)
            {
                ResourceCleanup cleanup = generation.Cleanups[index];
                if (!cleanup.Completed && (!buildOnly || cleanup.Lifetime == ResourceDependencyLifetime.Build))
                {
                    await cleanup.Callback().ConfigureAwait(false);
                    cleanup.Completed = true;
                }
            }

            foreach (ResourceSourceUse source in generation.Sources.Values)
            {
                if (source.Snapshot is { } snapshot && (!buildOnly || source.Lifetime == ResourceDependencyLifetime.Build))
                {
                    await snapshot.DisposeAsync().ConfigureAwait(false);
                    source.Snapshot = null;
                }
            }

            if (generation.BuildMemory is { } buildMemory)
            {
                await buildMemory.DisposeAsync().ConfigureAwait(false);
                generation.BuildMemory = null;
            }

            if (!buildOnly && generation.ResidentMemory is { } residentMemory)
            {
                await residentMemory.DisposeAsync().ConfigureAwait(false);
                generation.ResidentMemory = null;
            }

            lock (_gate)
            {
                foreach (ResourceDependency dependency in generation.Dependencies.Values)
                {
                    if (dependency.Generation is { } child && (!buildOnly || dependency.Lifetime == ResourceDependencyLifetime.Build))
                    {
                        child.Pins--;
                        dependency.Generation = null;
                    }
                }

                if (!buildOnly)
                {
                    generation.CleanupPhase = ResourceGenerationCleanupPhase.Complete;
                    generation.State = ResourceGenerationState.Released;
                    generation.Value = null;
                    _generations.Remove(generation);
                }
            }
        }
        catch
        {
            if (!buildOnly)
            {
                lock (_gate)
                {
                    generation.State = ResourceGenerationState.CleanupFailed;
                }
            }

            throw;
        }
    }

    private ResourceBuildView CreateView(HashSet<ResourceNode> forced, CancellationToken cancellationToken)
    {
        Dictionary<ResourceNode, ResourceGeneration> published = new(_published);
        Dictionary<ResourceNode, ResourceMetadata> metadata = new(_metadata);
        ResourceBuildView view = new(published, metadata, _publicationVersion, forced, cancellationToken);
        foreach (ResourceGeneration generation in published.Values)
        {
            generation.Pins++;
        }

        return view;
    }

    private async ValueTask EndViewAsync(ResourceBuildView view)
    {
        HashSet<ResourceGeneration> candidates;
        lock (_gate)
        {
            if (view.Roots < 0)
            {
                return;
            }

            if (view.Roots != 0)
            {
                throw new InvalidOperationException("A construction view cannot end while its root operations remain active.");
            }

            view.Roots = -1;
            candidates = view.Builds.Values.Select(static slot => slot.Generation).ToHashSet();
            foreach (ResourceGeneration generation in view.Published.Values)
            {
                generation.Pins--;
            }

            foreach (ResourceGeneration generation in candidates)
            {
                generation.Pins--;
            }

            // Failed build cleanup remains owned until an explicit trim or shutdown retry.
            candidates.RemoveWhere(static generation => generation.State is ResourceGenerationState.CleanupFailed or ResourceGenerationState.RetryingCleanup);
        }

        await ReleaseUnusedAsync(candidates, CancellationToken.None).ConfigureAwait(false);
    }

    private void SetPublished(Dictionary<ResourceNode, ResourceGeneration> updated)
    {
        ulong version = checked(_publicationVersion + 1);
        foreach (ResourceSlot slot in _slots)
        {
            if (slot.References == 0)
            {
                continue;
            }

            ResourceGeneration? replacement = updated.GetValueOrDefault(slot.Node);
            if (ReferenceEquals(slot.Current, replacement))
            {
                continue;
            }

            if (slot.Current is { } previous)
            {
                previous.Pins -= slot.References;
            }

            if (replacement is not null)
            {
                replacement.Pins += slot.References;
            }

            slot.Current = replacement;
        }

        _published = updated;
        _publicationVersion = version;
    }

    private void PruneMetadata()
    {
        HashSet<ResourceNode> reachable = [];
        Stack<ResourceNode> pending = new(_published.Keys.Concat(_invalidated).Concat(_slotByKey.Keys)
            .Concat(_loads.Keys).Concat(_generations.Select(static generation => generation.Node)));
        while (pending.TryPop(out ResourceNode node))
        {
            if (!reachable.Add(node) || !_metadata.TryGetValue(node, out ResourceMetadata? metadata))
            {
                continue;
            }

            foreach (ResourceNode dependency in metadata.Dependencies)
            {
                pending.Push(dependency);
            }
        }

        foreach (ResourceNode node in _metadata.Keys.Where(node => !reachable.Contains(node)).ToArray())
        {
            _metadata.Remove(node);
        }
    }
}
