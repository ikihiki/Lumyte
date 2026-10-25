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
    /// <remarks>Failed stream creation can retain a factory lease; call <see cref="TrimAsync"/> to retry its cleanup before shutdown.</remarks>
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

            if (_externalOwners != 0 || _operations != (_hotReload is null ? 0 : _reloadOperations) || _loads.Count != 0 || _sessions.Count != 0)
            {
                throw new InvalidOperationException("Return resource references, leases, resource snapshots and sessions, and complete all operations before disposing the manager. Retry failed session cleanup with TrimAsync first.");
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
            if (_hotReload is not null)
            {
                await _hotReload.DisposeAsync().ConfigureAwait(false);
            }

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
                    _watchTargets?.Clear();
                    _metadataReferences.Clear();
                    _metadataPruneCandidates.Clear();
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
        IAsyncDisposable[] failedSessions;
        lock (_gate)
        {
            failedSessions = _failedSessions.ToArray();
        }

        List<Exception>? errors = null;
        foreach (IAsyncDisposable session in failedSessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

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
        List<ResourceGeneration> wave = [];
        List<ResourceNode> removed = [];
        List<Exception>? errors = null;
        int released = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                wave.Clear();
                removed.Clear();
                Dictionary<ResourceNode, ResourceGeneration>? updated = null;
                foreach (ResourceGeneration generation in candidates ?? _generations)
                {
                    if (generation.State is not (ResourceGenerationState.Ready or ResourceGenerationState.CleanupFailed)
                        || generation.Pins != 0 || attempted.Contains(generation)
                        || (candidates is not null && (generation.State != ResourceGenerationState.Ready
                            || ReferenceEquals(_published.GetValueOrDefault(generation.Node), generation))))
                    {
                        continue;
                    }

                    if (ReferenceEquals(_published.GetValueOrDefault(generation.Node), generation))
                    {
                        updated ??= new(_published);
                        updated.Remove(generation.Node);
                        removed.Add(generation.Node);
                    }

                    generation.State = generation.State == ResourceGenerationState.CleanupFailed
                        ? ResourceGenerationState.RetryingCleanup
                        : ResourceGenerationState.Releasing;
                    attempted.Add(generation);
                    wave.Add(generation);
                }

                if (updated is not null)
                {
                    SetPublished(updated, removed);
                }
            }

            if (wave.Count == 0)
            {
                break;
            }

            int index = 0;
            try
            {
                for (; index < wave.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await CleanupGenerationAsync(wave[index], false).ConfigureAwait(false);
                        released++;
                    }
                    catch (Exception error)
                    {
                        (errors ??= []).Add(error);
                    }
                }
            }
            finally
            {
                // Cancellation must not strand reserved entries that have not started cleanup.
                lock (_gate)
                {
                    for (; index < wave.Count; index++)
                    {
                        ResourceGeneration generation = wave[index];
                        generation.State = generation.State == ResourceGenerationState.RetryingCleanup
                            ? ResourceGenerationState.CleanupFailed
                            : ResourceGenerationState.Ready;
                    }
                }
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more resource generations could not be released.", errors);
        }

        return released;
    }

    private async ValueTask CleanupGenerationAsync(ResourceGeneration generation, bool buildOnly, bool preserveContext = false)
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
                    if (preserveContext)
                    {
                        await generation.Stage.ReleaseAsync(output, generation.Runtime);
                    }
                    else
                    {
                        await generation.Executor.ReleaseAsync(generation.Stage, generation.Runtime, output).ConfigureAwait(false);
                    }
                }

                generation.CleanupPhase = ResourceGenerationCleanupPhase.Resources;
            }

            for (int index = generation.Cleanups.Count - 1; index >= 0; index--)
            {
                ResourceCleanup cleanup = generation.Cleanups[index];
                if (!cleanup.Completed && (!buildOnly || cleanup.Lifetime == ResourceDependencyLifetime.Build))
                {
                    await cleanup.ExecuteAsync().ConfigureAwait(preserveContext);
                    cleanup.Complete();
                }
            }

            foreach (ResourceSourceUse source in generation.Sources.Values)
            {
                if (source.Snapshot is { } snapshot && (!buildOnly || source.Lifetime == ResourceDependencyLifetime.Build))
                {
                    await snapshot.DisposeAsync().ConfigureAwait(preserveContext);
                    source.Snapshot = null;
                }
            }

            if (generation.BuildMemory is { } buildMemory)
            {
                await buildMemory.DisposeAsync().ConfigureAwait(preserveContext);
                generation.BuildMemory = null;
            }

            if (!buildOnly && generation.ResidentMemory is { } residentMemory)
            {
                await residentMemory.DisposeAsync().ConfigureAwait(preserveContext);
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
                    _failedGenerations.Remove(generation);
                    int remaining = _generationCounts[generation.Node] - 1;
                    if (remaining == 0)
                    {
                        _generationCounts.Remove(generation.Node);
                        _metadataPruneCandidates.Add(generation.Node);
                    }
                    else
                    {
                        _generationCounts[generation.Node] = remaining;
                    }
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
                    _failedGenerations.Add(generation);
                }
            }

            throw;
        }
    }

    private ResourceBuildView CreateView(HashSet<ResourceNode> forced, CancellationToken cancellationToken)
    {
        // Published maps are immutable after publication. Metadata is copied only before its next mutation.
        Dictionary<ResourceNode, ResourceGeneration> published = _published;
        _metadataShared = true;
        ResourceBuildView view = new(published, _metadata, _publicationVersion, forced, cancellationToken);
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
            if (_watchTargets is { } targets)
            {
                foreach (ResourceBuildSlot slot in view.Builds.Values)
                {
                    if (slot.Generation.Sources.Count != 0)
                    {
                        targets.CompleteBuild(slot.Generation.Sources.Keys);
                    }
                }
            }

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

    private void SetPublished(Dictionary<ResourceNode, ResourceGeneration> updated, IEnumerable<ResourceNode> changed)
    {
        ulong version = checked(_publicationVersion + 1);
        foreach (ResourceNode node in changed)
        {
            if (!_slotByKey.TryGetValue(node, out uint index))
            {
                continue;
            }

            ResourceSlot slot = _slots[(int)index];
            ResourceGeneration? replacement = updated.GetValueOrDefault(node);
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

    private void EnsureWritableMetadata()
    {
        if (_metadataShared)
        {
            _metadata = new(_metadata);
            _metadataShared = false;
        }
    }

    private void SetMetadata(ResourceNode node, ResourceMetadata metadata)
    {
        EnsureWritableMetadata();
        if (_metadata.TryGetValue(node, out ResourceMetadata? previous))
        {
            foreach (ResourceNode dependency in previous.Dependencies)
            {
                RemoveMetadataReference(dependency);
                _metadataPruneCandidates.Add(dependency);
            }
        }

        if (metadata.Sources.Count != 0 || previous?.Sources.Count > 0)
        {
            _watchTargets?.ReplaceMetadata(previous, metadata, _hotReload!);
        }

        _metadata[node] = metadata;
        foreach (ResourceNode dependency in metadata.Dependencies)
        {
            _metadataReferences[dependency] = _metadataReferences.GetValueOrDefault(dependency) + 1;
        }
    }

    private void RemoveMetadata(ResourceNode node, ResourceMetadata metadata)
    {
        EnsureWritableMetadata();
        _metadata.Remove(node);
        if (metadata.Sources.Count != 0)
        {
            _watchTargets?.RemoveMetadata(metadata);
        }

        foreach (ResourceNode dependency in metadata.Dependencies)
        {
            RemoveMetadataReference(dependency);
        }
    }

    private void RemoveMetadataReference(ResourceNode node)
    {
        int remaining = _metadataReferences[node] - 1;
        if (remaining == 0)
        {
            _metadataReferences.Remove(node);
        }
        else
        {
            _metadataReferences[node] = remaining;
        }
    }

    private bool IsMetadataRoot(ResourceNode node)
        => _published.ContainsKey(node) || _invalidated.Contains(node) || _slotByKey.ContainsKey(node)
            || _loads.ContainsKey(node) || _generationCounts.ContainsKey(node);

    private void PruneMetadata()
    {
        if (_metadataPruneCandidates.Count == 0)
        {
            return;
        }

        Stack<ResourceNode> pending = new(_metadataPruneCandidates);
        _metadataPruneCandidates.Clear();
        List<ResourceNode>? retained = null;
        while (pending.TryPop(out ResourceNode node))
        {
            if (IsMetadataRoot(node) || !_metadata.TryGetValue(node, out ResourceMetadata? metadata))
            {
                continue;
            }

            if (_metadataReferences.ContainsKey(node))
            {
                (retained ??= []).Add(node);
                continue;
            }

            RemoveMetadata(node, metadata);
            foreach (ResourceNode dependency in metadata.Dependencies)
            {
                pending.Push(dependency);
            }
        }

        if (retained is not null)
        {
            foreach (ResourceNode node in retained)
            {
                if (_metadata.ContainsKey(node) && !IsMetadataRoot(node))
                {
                    // Preserve reachability semantics even if retained metadata contains an orphan cycle.
                    PruneUnreachableMetadata();
                    break;
                }
            }
        }
    }

    private void PruneUnreachableMetadata()
    {
        HashSet<ResourceNode> reachable = [];
        Stack<ResourceNode> pending = new(_published.Keys.Concat(_invalidated).Concat(_slotByKey.Keys)
            .Concat(_loads.Keys).Concat(_generationCounts.Keys));
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

        List<ResourceNode>? removed = null;
        foreach (ResourceNode node in _metadata.Keys)
        {
            if (!reachable.Contains(node))
            {
                (removed ??= []).Add(node);
            }
        }

        if (removed is not null)
        {
            foreach (ResourceNode node in removed)
            {
                RemoveMetadata(node, _metadata[node]);
            }
        }
    }
}
