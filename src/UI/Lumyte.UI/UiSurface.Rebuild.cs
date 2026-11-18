using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private Widget? _pendingRebuild;
    private Func<Widget>? _pendingViewportBuild;
    private bool _forceViewportRebuild;
    private bool _applyingRebuild;
    private List<RebuildPlan>? _viewportContentPlans;
    private UiProposedTree? _viewportProjection;
    private Dictionary<UiElement, UiElementContent> _viewportPreparedContent = new(ReferenceEqualityComparer.Instance);

    /// <summary>Queues a fresh root configuration for the next update boundary.</summary>
    /// <param name="root">The passive root declaration.</param>
    public void Rebuild(Widget root)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(root);
        if (ReferenceEquals(root, Root) || ReferenceEquals(root, _pendingRebuild))
        {
            return;
        }

        ValidateRoot(root);
        _pendingRebuild = root;
        _pendingViewportBuild = null;
        _forceViewportRebuild = false;
    }

    /// <summary>Adopts prepared configuration changes without computing layout; virtual item models await UpdateLayout.</summary>
    public void UpdateTree()
    {
        using UiProfiling.Scope? profile = UiProfiling.Begin(1);
        try
        {
            ThrowIfDisposed();
            ThrowSignalFailure();
            if (_applyingRebuild)
            {
                profile?.Skip();
                return;
            }

            if (_pendingRebuild is { } virtualCandidate && (_pendingViewportBuild is not null || _forceViewportRebuild || CollectElements(_rootElement).Any(element => element.Widget.RequiresViewportPreparation) || ContainsViewportDeclaration(virtualCandidate)))
            {
                return;
            }

            if (_pendingRebuild is { } candidate)
            {
                _applyingRebuild = true;
                try
                {
                    using var plan = new RebuildPlan(_rootElement, candidate, validators: _treeValidators, shared: _sharedElements, prepareArguments: PrepareAnimationUpdate);
                    if (plan.Updates.Any(update => update.Next.RequiresViewportPreparation))
                    {
                        _forceViewportRebuild = true;
                        return;
                    }

                    PrepareAnimationUpdates(plan);
                    plan.Prepare();
                    PrepareSignalUpdates(plan);
                    if (_isDisposed)
                    {
                        return;
                    }

                    if (ReferenceEquals(_pendingRebuild, candidate))
                    {
                        _pendingRebuild = null;
                    }

                    _pendingViewportElements.Clear();
                    Publish(plan);
                }
                finally
                {
                    _applyingRebuild = false;
                }
            }

            ProcessContentRefreshes();
            ProcessSignals(true);
            ReconcileVisuals();
        }
        catch (Exception error)
        {
            profile?.Fail(error);
            throw;
        }
    }

    internal void RebuildSubtree(UiElement previous, Widget next)
    {
        ThrowIfDisposed();
        if (!ReferenceEquals(previous.Surface, this))
        {
            throw new ArgumentException("The target must be mounted on this surface.", nameof(previous));
        }

        if (_applyingRebuild || _pendingRebuild is not null)
        {
            return;
        }

        ValidateRoot(next);
        _applyingRebuild = true;
        try
        {
            using var plan = new RebuildPlan(previous, next, true, _rootElement, _treeValidators, _sharedElements, PrepareAnimationUpdate);
            PrepareAnimationUpdates(plan);
            plan.Prepare();
            PrepareSignalUpdates(plan);
            if (!_isDisposed)
            {
                Publish(plan);
            }
        }
        finally
        {
            _applyingRebuild = false;
        }
    }

    internal UiElementContent? PreparedViewportContent(UiElement element) => _viewportPreparedContent.GetValueOrDefault(element);

    internal void RefreshElementContent(UiElement element)
    {
        ThrowIfDisposed();
        if (_layoutPass is not null || _applyingRebuild || _dispatchDepth != 0 || _recordDepth != 0 || !ReferenceEquals(element.PreparationSurface, this))
        {
            throw new InvalidOperationException("Content preparation requires an idle owning surface.");
        }

        _applyingRebuild = true;
        try
        {
            if (_viewportContentPlans is { } proposals)
            {
                proposals.Add(new RebuildPlan(element, _viewportProjection?.Configuration(element) ?? element.Widget, true, _rootElement, _treeValidators, _sharedElements, PrepareAnimationUpdate, refreshContent: true, baseline: _viewportProjection));
                return;
            }

            using var plan = new RebuildPlan(element, element.Widget, true, _rootElement, _treeValidators, _sharedElements, PrepareAnimationUpdate, refreshContent: true);
            PrepareAnimationUpdates(plan);
            plan.Prepare();
            PrepareSignalUpdates(plan);
            if (!_isDisposed)
            {
                Publish(plan);
            }
        }
        finally
        {
            _applyingRebuild = false;
        }
    }

    private void Publish(RebuildPlan plan, bool drainAdoption = false)
    {
        plan.FinalizeScrollBindings();
        UiProfiling.Add(1, 101, plan.Updates.Count);
        UiProfiling.Add(1, 102, plan.Removed.Length);
        foreach (ElementUpdate update in plan.Updates)
        {
            if (update.Previous?.SharedElementKey is { } previous)
            {
                _sharedElements.Remove(previous);
            }
        }

        foreach (UiElement removed in plan.Removed)
        {
            if (removed.Widget.SharedElementKey is { } previous)
            {
                _sharedElements.Remove(previous);
            }
        }

        foreach (ElementUpdate update in plan.Updates)
        {
            if (update.Next.SharedElementKey is { } key)
            {
                _sharedElements.Add(key, update.Element);
            }
        }

        bool structureChanged = plan.Removed.Length != 0;
        foreach (ElementUpdate update in plan.Updates)
        {
            structureChanged |= update.ChildrenChanged || update.Previous is null;
            update.Element.Publish(this, update.Next, update.Slots);
            update.Element.Contexts = update.Contexts;
            if (update.Content is not null)
            {
                update.Element.ContentReads = update.Content.Reads;
                update.Element.FreshItemRealizations = update.Content.FreshRealizations;
            }

            update.Element.ConnectPresence(this, update.Previous);
            update.Element.UsesTreeValidation = update.UsesTreeValidation;
            if (update.UsesTreeValidation)
            {
                _treeValidators.Add(update.Element);
            }
            else
            {
                _treeValidators.Remove(update.Element);
            }
        }

        foreach (ElementUpdate update in plan.Updates)
        {
            foreach (IReadOnlyList<UiElement> slot in update.Slots)
            {
                foreach (UiElement child in slot)
                {
                    child.AssignParent(update.Element);
                }
            }
        }

        foreach (ElementUpdate update in plan.Updates)
        {
            if ((update.Invalidation & UiInvalidation.Layout) != 0)
            {
                update.Element.MarkLayoutDirty();
            }

            if ((update.Invalidation & (UiInvalidation.Layout | UiInvalidation.Input)) != 0)
            {
                InvalidateInput();
            }
        }

        InvalidateVisualRevision();
        foreach (ElementUpdate update in plan.Updates)
        {
            if (update.Previous is not null && ((update.Invalidation & (UiInvalidation.Layout | UiInvalidation.Visual)) != 0 || update.ChildrenChanged))
            {
                update.Element.InvalidateChildVisuals();
            }
        }

        foreach ((UiElement owner, IReadOnlyList<UiElement> itemRoots) in plan.CollectionItemRootUpdates)
        {
            owner.CollectionItemRoots = itemRoots;
        }

        _rootElement = plan.Root;
        _rootElement.HasCollectionItemOwnership = plan.HasCollectionItemOwnership;
        TreeRevision = checked(TreeRevision + 1);
        if (structureChanged)
        {
            StructureRevision = checked(StructureRevision + 1);
        }

        _inputReset++;
        foreach (UiElement removed in plan.Removed)
        {
            _treeValidators.Remove(removed);
        }

        List<Exception>? failures = null;

        void Apply(Action? commit)
        {
            if (!drainAdoption)
            {
                commit?.Invoke();
                return;
            }

            foreach (Action action in commit?.GetInvocationList() ?? [])
            {
                if (_isDisposed)
                {
                    break;
                }

                try
                {
                    action();
                }
                catch (Exception error)
                {
                    (failures ??= []).Add(error);
                }
            }
        }

        try
        {
            foreach (ElementUpdate update in plan.Updates)
            {
                Apply(update.Animations is { } animations ? animations.Commit : null);
                Apply(update.Signals is { } signals ? signals.Commit : null);
            }

            if (drainAdoption)
            {
                // Mandatory component state must match every published subtree before user commits run.
                foreach (ElementUpdate update in plan.Updates)
                {
                    Apply(update.ContentCommit);
                }
            }

            if (!_isDisposed)
            {
                Apply(() => UpdateHostValidation(plan));
            }

            foreach (ElementUpdate update in plan.Updates)
            {
                if (_isDisposed)
                {
                    break;
                }

                Apply(drainAdoption ? update.Commit : update.ContentCommit + update.Commit);
            }
        }
        catch (Exception error)
        {
            (failures ??= []).Add(error);
        }
        finally
        {
            try
            {
                CancelChangedInput(plan);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }

            try
            {
                Unmount(plan.Removed);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        ThrowFailures(failures);
        foreach (ElementUpdate update in plan.Updates)
        {
            if (_isDisposed)
            {
                break;
            }

            update.Element.NotifyPublished(update.Previous, update.ChildrenChanged);
        }

        FreezeHiddenFrameAnimations();
    }
}
