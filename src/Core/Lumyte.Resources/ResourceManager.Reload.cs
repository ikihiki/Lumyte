namespace Lumyte.Resources;

/// <summary>Coordinates transactional source graph publication.</summary>
public sealed partial class ResourceManager
{
    /// <summary>Rebuilds the graph affected by changed input revisions and publishes it atomically.</summary>
    /// <param name="changedSources">Source identifiers to recheck; notifications are only hints.</param>
    /// <param name="cancellationToken">Cancels candidate construction before publication.</param>
    /// <returns>The publication outcome.</returns>
    internal ValueTask<ResourceReloadResult> ReloadAsync(IReadOnlyCollection<ResourceId> changedSources, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changedSources);
        foreach (ResourceId id in changedSources)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        }

        return ReloadCoreAsync(changedSources, cancellationToken);
    }

    /// <summary>Rechecks all tracked input revisions and atomically rebuilds the graph affected by changes.</summary>
    /// <remarks>Includes provenance of trimmed build-only inputs. Resources that have never been loaded are not discovered or built.</remarks>
    /// <param name="cancellationToken">Cancels candidate construction before publication.</param>
    /// <returns>The publication outcome.</returns>
    internal ValueTask<ResourceReloadResult> ReloadAllAsync(CancellationToken cancellationToken = default)
        => ReloadCoreAsync([], cancellationToken, recheckAllSources: true);

    internal ValueTask<ResourceReloadResult> ReloadFromChangesAsync(IReadOnlyCollection<ResourceId> changes, bool rescan, CancellationToken cancellationToken)
        => ReloadCoreAsync(changes, cancellationToken, recheckAllSources: rescan, automatic: true);

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

    private async ValueTask<ResourceReloadResult> ReloadCoreAsync(IReadOnlyCollection<ResourceId> changedSources, CancellationToken cancellationToken, bool recheckAllSources = false, bool automatic = false)
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
                    if (metadata.Sources.Any(pair => revisions.TryGetValue(pair.Key, out string? revision) && revision != pair.Value))
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

                roots = affected.Where(node => view.Published.ContainsKey(node) || _slotByKey.ContainsKey(node)).ToArray();
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
                lock (_gate)
                {
                    EnsureRunning();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!validSources || _publicationVersion != view.PublicationVersion || HasUnrebuiltSourceDependents(view, used))
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
