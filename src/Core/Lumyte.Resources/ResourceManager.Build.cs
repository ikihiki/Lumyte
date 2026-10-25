using System.Runtime.ExceptionServices;
using Lumyte.Memory;

namespace Lumyte.Resources;

/// <summary>Builds typed resource graphs and records ownership before publication.</summary>
public sealed partial class ResourceManager
{
    internal async ValueTask<T> RequireAsync<T>(ResourceBuildView view, ResourceGeneration owner, ResourceBuildContext requester, ResourceKey<T> key, ResourceDependencyLifetime lifetime, CancellationToken cancellationToken)
        where T : notnull
    {
        ValidateLifetime(lifetime);
        cancellationToken.ThrowIfCancellationRequested();
        ResourceNode node = GetNode(key);
        ResourceDependency dependency;
        Task<ResourceGeneration> completion;
        bool start = false;
        lock (_gate)
        {
            EnsureRegistrationOpen(owner);
            AddEdge(view, owner.Node, node);
            if (!owner.Dependencies.TryGetValue(node, out dependency!))
            {
                dependency = new(lifetime);
                owner.Dependencies.Add(node, dependency);
                start = true;
            }
            else if (lifetime == ResourceDependencyLifetime.Resident)
            {
                dependency.Lifetime = lifetime;
            }

            completion = dependency.Completion!.Task;
        }

        if (start)
        {
            _ = ResolveDependencyAsync(view, node, dependency, requester);
        }

        ResourceGeneration generation = await completion.WaitAsync(cancellationToken).ConfigureAwait(requester.CanSharePipeline);
        lock (_gate)
        {
            ThrowIfUnavailable(generation);
            return (T)generation.Value!;
        }
    }

    internal ValueTask<ResourceSourceSnapshot> OpenSourceAsync(ResourceBuildView view, ResourceGeneration owner, ResourceId id, ResourceDependencyLifetime lifetime, CancellationToken cancellationToken)
    {
        ValidateLifetime(lifetime);
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        cancellationToken.ThrowIfCancellationRequested();
        ResourceSourceUse source;
        Task<ResourceSourceSnapshot> completion;
        bool start = false;
        lock (_gate)
        {
            EnsureRegistrationOpen(owner);
            if (!owner.Sources.TryGetValue(id, out source!))
            {
                source = new(lifetime);
                owner.Sources.Add(id, source);
                _watchTargets.Track(id);
                start = true;
            }
            else if (lifetime == ResourceDependencyLifetime.Resident)
            {
                source.Lifetime = lifetime;
            }

            completion = source.Completion!.Task;
        }

        if (start)
        {
            _ = ResolveSourceAsync(view, source, id);
        }

        return new(completion.WaitAsync(cancellationToken));
    }

    internal void RegisterCleanup(ResourceGeneration owner, Func<ValueTask> cleanup, ResourceDependencyLifetime lifetime)
    {
        ValidateLifetime(lifetime);
        lock (_gate)
        {
            EnsureRegistrationOpen(owner);
            owner.Cleanups.Add(new(cleanup, lifetime));
        }
    }

    internal void RegisterCleanup(ResourceGeneration owner, IDisposable cleanup, ResourceDependencyLifetime lifetime)
    {
        ValidateLifetime(lifetime);
        lock (_gate)
        {
            EnsureRegistrationOpen(owner);
            owner.Cleanups.Add(new(cleanup, lifetime));
        }
    }

    internal IMemoryScope GetMemoryScope(ResourceGeneration owner, ResourceDependencyLifetime lifetime)
    {
        ValidateLifetime(lifetime);
        lock (_gate)
        {
            EnsureRegistrationOpen(owner);
            IMemoryScope? existing = lifetime == ResourceDependencyLifetime.Build ? owner.BuildMemory : owner.ResidentMemory;
            if (existing is not null)
            {
                return existing;
            }
        }

        // Build contexts are used by their stage; scope creation never runs under the manager gate.
        IMemoryScope created = _memory.CreateScope($"{_name}/{owner.Node}/{owner.Revision}/{lifetime}");
        lock (_gate)
        {
            if (lifetime == ResourceDependencyLifetime.Build)
            {
                if (owner.BuildMemory is null)
                {
                    owner.BuildMemory = created;
                    return created;
                }
            }
            else if (owner.ResidentMemory is null)
            {
                owner.ResidentMemory = created;
                return created;
            }

            // A concurrently requested redundant scope is retained as owned cleanup.
            owner.Cleanups.Add(new((Func<ValueTask>)created.DisposeAsync, lifetime));
            return lifetime == ResourceDependencyLifetime.Build ? owner.BuildMemory! : owner.ResidentMemory!;
        }
    }

    internal void TrackRuntimeDependency(ResourceGeneration owner, string dependencyId, ulong expectedVersion, ResourceDependencyLifetime lifetime, object? providerIdentity)
    {
        ValidateLifetime(lifetime);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyId);
        if (!_runtimes.TryGetValue(dependencyId, out ResourceRuntimeState? runtime))
        {
            throw new ArgumentException($"Runtime dependency '{dependencyId}' is not registered.", nameof(dependencyId));
        }

        RefreshRuntime(runtime);
        lock (_gate)
        {
            EnsureRegistrationOpen(owner);
            if (providerIdentity is not null && !ReferenceEquals(runtime.Identity, providerIdentity))
            {
                throw new InvalidOperationException($"Runtime dependency '{dependencyId}' belongs to another service instance.");
            }

            ResourceRuntimeSnapshot snapshot = runtime.Snapshot;
            if (!snapshot.IsAvailable || expectedVersion == 0 || snapshot.Version != expectedVersion || expectedVersion <= snapshot.InvalidatedThroughVersion)
            {
                throw new ResourceUnavailableException(dependencyId, expectedVersion);
            }

            if (owner.Runtimes.TryGetValue(dependencyId, out ResourceRuntimeUse previous))
            {
                if (previous.Version != expectedVersion)
                {
                    throw new InvalidOperationException($"A build cannot combine versions of runtime '{dependencyId}'.");
                }

                if (previous.Lifetime == ResourceDependencyLifetime.Resident)
                {
                    lifetime = previous.Lifetime;
                }
            }

            owner.Runtimes[dependencyId] = new(expectedVersion, lifetime);
        }
    }

    internal Task CompleteContextOperationsAsync(ResourceGeneration generation)
    {
        lock (_gate)
        {
            generation.RegistrationsClosed = true;
            Task? pending = null;
            bool multiple = false;
            foreach (ResourceDependency dependency in generation.Dependencies.Values)
            {
                Task task = dependency.Completion!.Task;
                if (!task.IsCompletedSuccessfully)
                {
                    multiple |= pending is not null;
                    pending = task;
                }
            }

            foreach (ResourceSourceUse source in generation.Sources.Values)
            {
                Task task = source.Completion!.Task;
                if (!task.IsCompletedSuccessfully)
                {
                    multiple |= pending is not null;
                    pending = task;
                }
            }

            return multiple
                ? Task.WhenAll(generation.Dependencies.Values.Select(static dependency => (Task)dependency.Completion!.Task)
                    .Concat(generation.Sources.Values.Select(static source => (Task)source.Completion!.Task)))
                : pending ?? Task.CompletedTask;
        }
    }

    internal async ValueTask ExecuteStageAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        context.BeginExecution();
        ResourceGeneration generation = context.Generation;
        Exception? failure = null;
        try
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                object value = await generation.Stage.BuildAsync(context, cancellationToken);
                if (!generation.Stage.OutputType.IsInstanceOfType(value))
                {
                    throw new InvalidOperationException($"Pipeline for '{generation.Stage.OutputType}' returned an invalid output.");
                }

                lock (_gate)
                {
                    generation.Value = value;
                }
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                context.Close();
            }

            // Keep the job alive until all registered operations finish, including ignored or canceled waits.
            try
            {
                await CompleteContextOperationsAsync(generation);
            }
            catch (Exception error)
            {
                failure = failure is null ? error : ReferenceEquals(failure, error) ? failure : new AggregateException(failure, error);
            }

            lock (_gate)
            {
                // Callers already hold their task; the completed construction no longer needs its result references.
                foreach (ResourceDependency dependency in generation.Dependencies.Values)
                {
                    dependency.Completion = null;
                    if (generation.Value is { } output && ReferenceEquals(dependency.Generation?.Value, output))
                    {
                        // An ignored dependency may only become known while draining. Its generation owns the value.
                        generation.Value = null;
                        var aliasError = new InvalidOperationException("A stage cannot return a borrowed dependency as a separately owned output.");
                        failure = failure is null ? aliasError : new AggregateException(failure, aliasError);
                    }
                }

                foreach (ResourceSourceUse source in generation.Sources.Values)
                {
                    source.Completion = null;
                }
            }

            if (failure is null)
            {
                try
                {
                    await CleanupGenerationAsync(generation, true, true);
                    lock (_gate)
                    {
                        CaptureResidentRuntimes(generation);
                        generation.State = ResourceGenerationState.Ready;
                    }

                    return;
                }
                catch (Exception error)
                {
                    failure = error;
                }
            }

            try
            {
                await CleanupGenerationAsync(generation, false, true);
            }
            catch (Exception cleanupError)
            {
                failure = new AggregateException(failure, cleanupError);
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally
        {
            context.EndExecution();
        }
    }

    private static void EnsureRegistrationOpen(ResourceGeneration owner)
        => ObjectDisposedException.ThrowIf(owner.RegistrationsClosed, typeof(ResourceBuildContext));

    private static void ValidateLifetime(ResourceDependencyLifetime lifetime)
    {
        if (lifetime is not (ResourceDependencyLifetime.Build or ResourceDependencyLifetime.Resident))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }
    }

    private static bool FindPath(ResourceBuildView view, ResourceNode from, ResourceNode target, HashSet<ResourceNode> visited, List<ResourceNode> path)
    {
        if (!visited.Add(from))
        {
            return false;
        }

        path.Add(from);
        if (from == target)
        {
            return true;
        }

        IEnumerable<ResourceNode> edges = view.Edges.TryGetValue(from, out HashSet<ResourceNode>? current)
            ? current
            : view.Metadata.TryGetValue(from, out ResourceMetadata? metadata) ? metadata.Dependencies : [];
        foreach (ResourceNode dependency in edges)
        {
            if (FindPath(view, dependency, target, visited, path))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private static HashSet<ResourceNode> CollectReachable(ResourceBuildView view, IEnumerable<ResourceNode> roots)
    {
        HashSet<ResourceNode> visited = [];
        Stack<ResourceNode> pending = new(roots);
        while (pending.TryPop(out ResourceNode node))
        {
            if (!visited.Add(node))
            {
                continue;
            }

            IEnumerable<ResourceNode> edges = view.Edges.TryGetValue(node, out HashSet<ResourceNode>? current)
                ? current
                : view.Metadata.TryGetValue(node, out ResourceMetadata? metadata) ? metadata.Dependencies : [];
            foreach (ResourceNode dependency in edges)
            {
                pending.Push(dependency);
            }
        }

        return visited;
    }

    private async Task RunLoadAsync(ResourceNode node, ResourceLoad load)
    {
        ResourceGeneration? generation = null;
        Exception? failure = null;
        try
        {
            generation = await BuildNodeAsync(load.View, node).ConfigureAwait(false);
            HashSet<ResourceNode> used;
            lock (_gate)
            {
                used = CollectReachable(load.View, [node]);
            }

            if (!await ValidateSourcesAsync(load.View, used, CancellationToken.None).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"Input revisions changed while constructing {node}; reload and retry the request.");
            }

            RefreshUsedRuntimes(load.View, used);
            lock (_gate)
            {
                EnsureRunning();
                ValidateRuntimeVersions(load.View, used);
                if (!CanMerge(load.View, used))
                {
                    throw new InvalidOperationException($"Publication changed while constructing {node}; retry the request.");
                }

                foreach (ResourceNode candidate in used)
                {
                    if (!load.View.Builds.TryGetValue(candidate, out ResourceBuildSlot? built) || !built.Completion.Task.IsCompletedSuccessfully)
                    {
                        continue;
                    }

                    foreach ((ResourceId source, ResourceSourceUse use) in built.Generation.Sources)
                    {
                        foreach (ResourceMetadata metadata in _metadata.Values)
                        {
                            if (metadata.Sources.TryGetValue(source, out string? revision) && revision != use.Revision)
                            {
                                throw new InvalidOperationException($"Source '{source.Value}' changed; signal a source change to publish its dependent graph together.");
                            }
                        }
                    }
                }

                PublishCandidates(load.View, used);
                load.Generation = generation;
                generation.Pins += load.Waiters;
                _loads.Remove(node);
                _metadataPruneCandidates.Add(node);
            }
        }
        catch (Exception error)
        {
            failure = error;
        }

        bool finishView;
        lock (_gate)
        {
            load.View.Roots--;
            finishView = load.View.Roots == 0;
            if (finishView && ReferenceEquals(_normalView, load.View))
            {
                _normalView = null;
            }
        }

        if (finishView)
        {
            try
            {
                await EndViewAsync(load.View).ConfigureAwait(false);
            }
            catch (Exception cleanupError)
            {
                // A successful publication remains visible; report candidate cleanup failures on Trim.
                if (failure is not null)
                {
                    failure = new AggregateException(failure, cleanupError);
                }
            }
        }

        lock (_gate)
        {
            if (ReferenceEquals(_loads.GetValueOrDefault(node), load))
            {
                _loads.Remove(node);
                _metadataPruneCandidates.Add(node);
            }

            _operations--;
        }

        if (failure is null)
        {
            load.Completion.TrySetResult(generation!);
        }
        else
        {
            load.Completion.TrySetException(failure);
        }
    }

    private ValueTask<ResourceGeneration> BuildNodeAsync(ResourceBuildView view, ResourceNode node, ResourceBuildContext? requester = null)
    {
        ResourceBuildSlot slot;
        lock (_gate)
        {
            if (!view.Forced.Contains(node) && view.Published.TryGetValue(node, out ResourceGeneration? cached))
            {
                ThrowIfUnavailable(cached);
                return ValueTask.FromResult(cached);
            }

            if (view.Builds.TryGetValue(node, out slot!))
            {
                return new(slot.Completion.Task);
            }

            foreach (ResourceGeneration failed in _failedGenerations)
            {
                if (failed.Node == node)
                {
                    throw new InvalidOperationException($"Cleanup for {node} must complete before another build; call TrimAsync first.");
                }
            }

            (IResourceStage stage, IResourceRuntime runtime) = _stages[node.TypeHandle];
            ResourceGeneration candidate = new(node, checked(++_revision), stage, runtime, _executors[runtime]);
            slot = new(candidate);
            _generations.Add(candidate);
            _generationCounts[node] = _generationCounts.GetValueOrDefault(node) + 1;
            view.Builds.Add(node, slot);
            view.Edges[node] = [];
        }

        ResourceBuildContext? parent = requester is not null && requester.CanSharePipeline
            && ReferenceEquals(requester.Runtime, slot.Generation.Runtime) ? requester : null;
        _ = BuildSlotAsync(view, slot, parent);
        return new(slot.Completion.Task);
    }

    private async Task BuildSlotAsync(ResourceBuildView view, ResourceBuildSlot slot, ResourceBuildContext? parent)
    {
        ResourceGeneration generation = slot.Generation;
        ResourceBuildContext context = new(this, view, generation, parent);
        try
        {
            view.CancellationToken.ThrowIfCancellationRequested();
            if (parent is null)
            {
                await generation.Executor.BuildAsync(context, view.CancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (context.InlineDepth >= 32)
                {
                    await Task.Yield();
                    context.InlineDepth = 0;
                }

                await context.ExecuteAsync(view.CancellationToken);
                if (parent.ShouldYield)
                {
                    // Keep the fairness suspension inside the tracked dependency build, even if its caller did not await it.
                    await Task.Yield();
                }
            }

            if (!context.IsClosed)
            {
                throw new InvalidOperationException("The executor must run the context before completing construction.");
            }

            slot.Completion.TrySetResult(generation);
        }
        catch (Exception error)
        {
            // Rejected or canceled admission never entered the stage procedure.
            if (!context.IsClosed)
            {
                context.Close();
                try
                {
                    await CleanupGenerationAsync(generation, false).ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    error = new AggregateException(error, cleanupError);
                }
            }

            slot.Completion.TrySetException(error);
        }
    }

    private async Task ResolveDependencyAsync(ResourceBuildView view, ResourceNode node, ResourceDependency dependency, ResourceBuildContext requester)
    {
        TaskCompletionSource<ResourceGeneration> completion = dependency.Completion!;
        try
        {
            ResourceGeneration generation = await BuildNodeAsync(view, node, requester).ConfigureAwait(requester.CanSharePipeline);
            lock (_gate)
            {
                generation.Pins++;
                dependency.Generation = generation;
            }

            completion.TrySetResult(generation);
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private async Task ResolveSourceAsync(ResourceBuildView view, ResourceSourceUse use, ResourceId id)
    {
        TaskCompletionSource<ResourceSourceSnapshot> completion = use.Completion!;
        try
        {
            ResourceSourceSnapshot snapshot = await _source.OpenSnapshotAsync(id, view.CancellationToken).ConfigureAwait(false);

            // Ownership is recorded before inspecting provider metadata so malformed snapshots can be cleaned up.
            use.Snapshot = snapshot;
            string revision = snapshot.Revision;
            if (snapshot.Id != id || string.IsNullOrEmpty(revision))
            {
                throw new InvalidOperationException($"Source '{id.Value}' returned an invalid snapshot identity or revision.");
            }

            lock (_gate)
            {
                use.Revision = revision;
                if (view.SourceRevisions.TryGetValue(id, out string? previous) && previous != use.Revision)
                {
                    throw new InvalidOperationException($"Source '{id.Value}' changed within one construction view.");
                }

                view.SourceRevisions[id] = use.Revision;
            }

            completion.TrySetResult(snapshot);
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private void AddEdge(ResourceBuildView view, ResourceNode from, ResourceNode to)
    {
        if (view.Edges[from].Contains(to))
        {
            return;
        }

        HashSet<ResourceNode> visited = [];
        List<ResourceNode> path = [];
        if (FindPath(view, to, from, visited, path))
        {
            throw new InvalidOperationException($"Resource dependency cycle: {from} -> {string.Join(" -> ", path)}");
        }

        view.Edges[from].Add(to);
    }

    private async ValueTask<bool> ValidateSourcesAsync(ResourceBuildView view, HashSet<ResourceNode> used, CancellationToken cancellationToken)
    {
        Dictionary<ResourceId, string> sources = [];
        lock (_gate)
        {
            foreach (ResourceNode node in used)
            {
                IEnumerable<KeyValuePair<ResourceId, string>> provenance = view.Builds.TryGetValue(node, out ResourceBuildSlot? built)
                    ? built.Generation.Sources.Select(static pair => KeyValuePair.Create(pair.Key, pair.Value.Revision))
                    : view.Metadata.TryGetValue(node, out ResourceMetadata? metadata) ? metadata.Sources : [];
                foreach ((ResourceId id, string revision) in provenance)
                {
                    if (sources.TryGetValue(id, out string? previous) && previous != revision)
                    {
                        return false;
                    }

                    sources[id] = revision;
                }
            }
        }

        foreach ((ResourceId id, string revision) in sources)
        {
            if (await _source.GetRevisionAsync(id, cancellationToken).ConfigureAwait(false) != revision)
            {
                return false;
            }
        }

        return true;
    }

    private void RefreshUsedRuntimes(ResourceBuildView view, HashSet<ResourceNode> used)
    {
        string[] dependencies;
        lock (_gate)
        {
            dependencies = used.SelectMany(node => view.Builds.TryGetValue(node, out ResourceBuildSlot? built)
                ? built.Generation.Runtimes.Keys
                : view.Metadata.TryGetValue(node, out ResourceMetadata? metadata) ? metadata.Runtimes.Keys : []).Distinct(StringComparer.Ordinal).ToArray();
        }

        foreach (string dependency in dependencies)
        {
            RefreshRuntime(_runtimes[dependency]);
        }
    }

    private void ValidateRuntimeVersions(ResourceBuildView view, HashSet<ResourceNode> used)
    {
        foreach (ResourceNode node in used)
        {
            if (!view.Builds.TryGetValue(node, out ResourceBuildSlot? built))
            {
                if (view.Published.TryGetValue(node, out ResourceGeneration? existing))
                {
                    ThrowIfUnavailable(existing);
                }

                continue;
            }

            foreach ((string id, ResourceRuntimeUse usage) in built.Generation.Runtimes)
            {
                ResourceRuntimeSnapshot current = _runtimes[id].Snapshot;
                if (!current.IsAvailable || current.Version != usage.Version || usage.Version <= current.InvalidatedThroughVersion)
                {
                    throw new ResourceUnavailableException(id, usage.Version);
                }
            }
        }
    }

    private bool CanMerge(ResourceBuildView view, HashSet<ResourceNode> used)
    {
        foreach (ResourceNode node in used)
        {
            ResourceGeneration? previous = view.Published.GetValueOrDefault(node);
            ResourceGeneration? current = _published.GetValueOrDefault(node);
            if (!ReferenceEquals(current, previous)
                && (!view.Builds.TryGetValue(node, out ResourceBuildSlot? candidate) || !ReferenceEquals(current, candidate.Generation)))
            {
                return false;
            }
        }

        return true;
    }

    private void PublishCandidates(ResourceBuildView view, HashSet<ResourceNode> used)
    {
        Dictionary<ResourceNode, ResourceGeneration>? map = null;
        foreach (ResourceNode node in used)
        {
            if (!view.Builds.TryGetValue(node, out ResourceBuildSlot? candidate))
            {
                continue;
            }

            ResourceGeneration generation = candidate.Generation;
            if (!candidate.Completion.Task.IsCompletedSuccessfully || generation.State != ResourceGenerationState.Ready)
            {
                throw new InvalidOperationException($"An incomplete candidate for {node} cannot be published.");
            }

            if (ReferenceEquals(_published.GetValueOrDefault(node), generation))
            {
                continue;
            }

            map ??= new(_published);
            map[node] = generation;
            SetMetadata(node, new(
                generation.Dependencies.Keys.ToArray(),
                generation.Sources.ToDictionary(static pair => pair.Key, static pair => pair.Value.Revision),
                new Dictionary<string, ResourceRuntimeUse>(generation.Runtimes, StringComparer.Ordinal)));
            _invalidated.Remove(node);
        }

        if (map is not null)
        {
            SetPublished(map, used);
        }

        if (_watchRescanRequested)
        {
            _watchRescanRequested = false;

            // A change hint can arrive before an initial build has published its provenance.
            _hotReload?.RequestRescan();
        }
    }
}
