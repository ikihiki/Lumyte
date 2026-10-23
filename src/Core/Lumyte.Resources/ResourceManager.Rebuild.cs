namespace Lumyte.Resources;

/// <summary>Coordinates transactional source and runtime graph publication.</summary>
public sealed partial class ResourceManager
{
    /// <summary>Rebuilds resources affected by runtime generation changes, including invalidated handles.</summary>
    /// <param name="dependencyIds">The registered runtimes whose current versions should be used.</param>
    /// <param name="cancellationToken">Cancels candidate construction before publication.</param>
    /// <returns>The publication outcome.</returns>
    public ValueTask<ResourceReloadResult> RebuildRuntimeAsync(IReadOnlyCollection<string> dependencyIds, CancellationToken cancellationToken = default)
    {
        foreach (string id in dependencyIds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            if (!_runtimes.ContainsKey(id))
            {
                throw new ArgumentException($"Runtime dependency '{id}' is not registered.", nameof(dependencyIds));
            }
        }

        return ReloadCoreAsync([], dependencyIds, cancellationToken);
    }

    private static bool HasUnrebuiltSourceDependents(ResourceBuildView view, HashSet<ResourceNode> used)
    {
        foreach (ResourceNode node in used)
        {
            if (!view.Builds.TryGetValue(node, out ResourceBuildSlot? candidate))
            {
                continue;
            }

            foreach ((ResourceId source, ResourceSourceUse use) in candidate.Generation.Sources)
            {
                if (view.Metadata.Any(pair => !view.Builds.ContainsKey(pair.Key)
                    && pair.Value.Sources.TryGetValue(source, out string? revision) && revision != use.Revision))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async ValueTask<ResourceReloadResult> ReloadCoreAsync(IReadOnlyCollection<ResourceId> changedSources, IReadOnlyCollection<string> dependencyIds, CancellationToken cancellationToken, bool recheckAllSources = false, bool automatic = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            EnsureRunning();
            _operations++;
            if (automatic)
            {
                _reloadOperations++;
            }
        }

        bool entered = false;
        ResourceBuildView? view = null;
        Exception? failure = null;
        ResourceReloadResult? result = null;
        try
        {
            await _maintenance.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            foreach (string id in dependencyIds.Distinct(StringComparer.Ordinal))
            {
                RefreshRuntime(_runtimes[id]);
            }

            lock (_gate)
            {
                EnsureRunning();
                view = CreateView([], cancellationToken);
            }

            Dictionary<ResourceId, string?> revisions = [];
            var trackedSources = view.Metadata.Values.SelectMany(static metadata => metadata.Sources.Keys).ToHashSet();
            IEnumerable<ResourceId> sources = recheckAllSources
                ? trackedSources
                : changedSources.Where(trackedSources.Contains).Distinct();
            foreach (ResourceId id in sources)
            {
                revisions[id] = await _source.GetRevisionAsync(id, cancellationToken).ConfigureAwait(false);
            }

            HashSet<ResourceNode> affected = [];
            ResourceNode[] roots;
            lock (_gate)
            {
                foreach ((ResourceNode node, ResourceMetadata metadata) in view.Metadata)
                {
                    if (metadata.Sources.Any(pair => revisions.TryGetValue(pair.Key, out string? revision) && revision != pair.Value)
                        || metadata.Runtimes.Any(pair => dependencyIds.Contains(pair.Key)
                            && (pair.Value.Version != _runtimes[pair.Key].Snapshot.Version
                                || !_runtimes[pair.Key].Snapshot.IsAvailable
                                || pair.Value.Version <= _runtimes[pair.Key].Snapshot.InvalidatedThroughVersion)))
                    {
                        affected.Add(node);
                    }
                }

                bool expanded;
                do
                {
                    expanded = false;
                    foreach ((ResourceNode node, ResourceMetadata metadata) in view.Metadata)
                    {
                        if (!affected.Contains(node) && metadata.Dependencies.Any(affected.Contains))
                        {
                            affected.Add(node);
                            expanded = true;
                        }
                    }
                }
                while (expanded);

                view.Forced.UnionWith(affected);
                foreach (ResourceNode node in affected)
                {
                    view.Edges[node] = [];
                }

                roots = affected.Where(node => view.Published.ContainsKey(node) || _invalidated.Contains(node) || _slotByKey.ContainsKey(node)).ToArray();
            }

            if (roots.Length == 0)
            {
                result = ReloadResult(ResourceReloadStatus.Unchanged, 0);
            }
            else
            {
                await Task.WhenAll(roots.Select(node => BuildNodeAsync(view, node))).ConfigureAwait(false);
                HashSet<ResourceNode> used;
                lock (_gate)
                {
                    used = CollectReachable(view, roots);
                }

                bool validSources = await ValidateSourcesAsync(view, used, cancellationToken).ConfigureAwait(false);
                RefreshUsedRuntimes(view, used);
                lock (_gate)
                {
                    EnsureRunning();
                    cancellationToken.ThrowIfCancellationRequested();
                    bool validRuntimes = true;
                    try
                    {
                        ValidateRuntimeVersions(view, used);
                    }
                    catch (ResourceUnavailableException)
                    {
                        validRuntimes = false;
                    }

                    if (!validSources || !validRuntimes || _publicationVersion != view.PublicationVersion || HasUnrebuiltSourceDependents(view, used))
                    {
                        result = ReloadResult(ResourceReloadStatus.Conflict, 0);
                    }
                    else
                    {
                        PublishCandidates(view, used);
                        result = ReloadResult(ResourceReloadStatus.Published, view.Builds.Count);
                    }
                }
            }
        }
        catch (Exception error)
        {
            failure = error;
        }
        finally
        {
            if (view is not null)
            {
                try
                {
                    // All candidates finish before their construction view and pins are returned.
                    Task[] builds;
                    lock (_gate)
                    {
                        builds = view.Builds.Values.Select(static slot => (Task)slot.Completion.Task).ToArray();
                    }

                    try
                    {
                        await Task.WhenAll(builds).ConfigureAwait(false);
                    }
                    catch
                    {
                        // The primary build failure is preserved above; cleanup has its own result.
                    }

                    await EndViewAsync(view).ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    if (result?.Status != ResourceReloadStatus.Published)
                    {
                        failure = failure is null ? cleanupError : new AggregateException(failure, cleanupError);
                    }
                }
            }

            if (entered)
            {
                _maintenance.Release();
            }

            lock (_gate)
            {
                _operations--;
                if (automatic)
                {
                    _reloadOperations--;
                }
            }
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result!;
    }

    private ResourceReloadResult ReloadResult(ResourceReloadStatus status, int updated)
    {
        lock (_gate)
        {
            return new() { Status = status, PublicationVersion = _publicationVersion, UpdatedResources = updated };
        }
    }
}
