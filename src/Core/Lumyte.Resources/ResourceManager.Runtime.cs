namespace Lumyte.Resources;

/// <summary>Tracks runtime availability and irreversible resource invalidation.</summary>
public sealed partial class ResourceManager
{
    internal bool IsUsable(ResourceGeneration generation)
    {
        RefreshRuntimes();
        lock (_gate)
        {
            EnsureRunning();
            return !TryGetUnavailable(generation, out _);
        }
    }

    internal void ValidateLease(ResourceGeneration generation)
    {
        RefreshRuntimes();
        lock (_gate)
        {
            EnsureRunning();
            ThrowIfUnavailable(generation);
        }
    }

    private static void ValidateRuntimeSnapshot(ResourceRuntimeSnapshot previous, ResourceRuntimeSnapshot next)
    {
        if (next.Version < previous.Version || next.InvalidatedThroughVersion < previous.InvalidatedThroughVersion)
        {
            throw new InvalidOperationException("Runtime versions and invalidation watermarks must not decrease or wrap.");
        }

        if (next.Version == previous.Version && next != previous)
        {
            throw new InvalidOperationException("A runtime snapshot must increase its version whenever its state changes.");
        }

        if (next.IsAvailable && (next.Version == 0 || next.Version <= next.InvalidatedThroughVersion))
        {
            throw new InvalidOperationException("An available runtime version must be nonzero and above its invalidation watermark.");
        }
    }

    private static void CaptureResidentRuntimes(ResourceGeneration generation)
    {
        // Dependencies are complete before Ready is published. Flatten their immutable
        // resident uses once, keeping distinct versions of the same runtime.
        HashSet<(string Id, ulong Version)>? resident = null;
        foreach ((string id, ResourceRuntimeUse use) in generation.Runtimes)
        {
            if (use.Lifetime == ResourceDependencyLifetime.Resident)
            {
                (resident ??= []).Add((id, use.Version));
            }
        }

        foreach (ResourceDependency dependency in generation.Dependencies.Values)
        {
            if (dependency.Lifetime == ResourceDependencyLifetime.Resident && dependency.Generation is { ResidentRuntimes.Length: > 0 } child)
            {
                (resident ??= []).UnionWith(child.ResidentRuntimes);
            }
        }

        generation.ResidentRuntimes = resident?.ToArray() ?? [];
    }

    private void RefreshRuntimes()
    {
        if (_runtimes.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            EnsureRunning();
            _operations++;
        }

        try
        {
            // Read provider metadata outside the gate, even when its Changed observer has not run yet.
            foreach (ResourceRuntimeState runtime in _runtimes.Values)
            {
                ObserveRuntime(runtime);
            }

            lock (_gate)
            {
                EnsureRunning();
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

    private void RefreshRuntime(ResourceRuntimeState runtime)
    {
        while (true)
        {
            ResourceRuntimeSnapshot previous;
            lock (_gate)
            {
                if (_closing || _disposed)
                {
                    return;
                }

                previous = runtime.Snapshot;
            }

            ResourceRuntimeSnapshot next = runtime.Provider.Snapshot;
            string id = runtime.Provider.Id;
            object identity = runtime.Provider.Identity;
            lock (_gate)
            {
                if (_closing || _disposed)
                {
                    return;
                }

                if (runtime.Snapshot != previous)
                {
                    continue;
                }

                if (!_runtimes.TryGetValue(id, out ResourceRuntimeState? registered)
                    || !ReferenceEquals(registered, runtime) || !ReferenceEquals(identity, runtime.Identity))
                {
                    throw new InvalidOperationException("A runtime provider must preserve its registered identifier and service identity.");
                }

                ValidateRuntimeSnapshot(previous, next);
                if (previous == next)
                {
                    return;
                }

                runtime.Snapshot = next;
                Dictionary<ResourceNode, ResourceGeneration>? updated = null;
                foreach ((ResourceNode node, ResourceGeneration generation) in _published)
                {
                    if (TryGetUnavailable(generation, out _))
                    {
                        updated ??= new(_published);
                        updated.Remove(node);
                        _invalidated.Add(node);
                    }
                }

                if (updated is not null)
                {
                    SetPublished(updated);
                }

                return;
            }
        }
    }

    private void ObserveRuntime(ResourceRuntimeState runtime)
    {
        try
        {
            RefreshRuntime(runtime);
        }
        catch (Exception error)
        {
            lock (_gate)
            {
                _runtimeFailure ??= error;
            }
        }
    }

    private void UnsubscribeRuntimes()
    {
        List<Exception>? errors = null;
        foreach (ResourceRuntimeState runtime in _runtimes.Values)
        {
            if (runtime.Handler is not { } handler)
            {
                continue;
            }

            try
            {
                runtime.Provider.Changed -= handler;
                runtime.Handler = null;
            }
            catch (Exception error)
            {
                (errors ??= []).Add(error);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more runtime subscriptions could not be removed.", errors);
        }
    }

    private bool TryGetUnavailable(ResourceGeneration generation, out (string Id, ulong Version) unavailable)
    {
        foreach ((string id, ulong version) in generation.ResidentRuntimes)
        {
            if (!_runtimes.TryGetValue(id, out ResourceRuntimeState? runtime)
                || !runtime.Snapshot.IsAvailable || version == 0
                || version > runtime.Snapshot.Version || version <= runtime.Snapshot.InvalidatedThroughVersion)
            {
                unavailable = (id, version);
                return true;
            }
        }

        unavailable = default;
        return false;
    }

    private void ThrowIfUnavailable(ResourceGeneration generation)
    {
        if (TryGetUnavailable(generation, out (string Id, ulong Version) unavailable))
        {
            throw new ResourceUnavailableException(unavailable.Id, unavailable.Version);
        }
    }

    private void ThrowIfKnownUnavailable(ResourceNode node)
    {
        if (_metadata.TryGetValue(node, out ResourceMetadata? metadata))
        {
            foreach ((string id, ResourceRuntimeUse use) in metadata.Runtimes)
            {
                if (use.Lifetime == ResourceDependencyLifetime.Resident
                    && (!_runtimes.TryGetValue(id, out ResourceRuntimeState? runtime) || !runtime.Snapshot.IsAvailable))
                {
                    throw new ResourceUnavailableException(id, use.Version);
                }
            }
        }
    }
}
