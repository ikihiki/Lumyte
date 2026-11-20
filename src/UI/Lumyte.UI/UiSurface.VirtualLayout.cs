namespace Lumyte.UI;

/// <summary>Prepares one unpublished viewport fixedpoint before accepting any state.</summary>
public sealed partial class UiSurface
{
    private readonly HashSet<UiElement> _pendingViewportElements = new(ReferenceEqualityComparer.Instance);
    private List<UiElement> _viewportRetirement = [];
    private UiElement? _viewportDeferredFocus;
    private long _viewportDeferredFocusRequest;
    private int _viewportPreparationDepth;
    private bool _preparingVirtualDeclaration;

    // Includes declaration construction before projected layout begins; excludes adopted callbacks.
    internal bool HasUnpublishedViewportPreparation => _preparingVirtualDeclaration || IsPreparingViewportLayout;

    internal UiElement? DeferredViewportFocus => _viewportDeferredFocusRequest == _focusRequest ? _viewportDeferredFocus : null;

    private static Dictionary<UiElement, UiElementContent> ViewportContentState(IEnumerable<ElementUpdate> updates)
    {
        var content = new Dictionary<UiElement, UiElementContent>(ReferenceEqualityComparer.Instance);
        foreach (ElementUpdate update in updates)
        {
            if (update.Content is { } prepared)
            {
                content.Add(update.Element, prepared);
            }
        }

        return content;
    }

    private bool UpdatePendingViewportElements()
    {
        var plans = new List<RebuildPlan>();
        var covered = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
        try
        {
            foreach (UiElement element in _pendingViewportElements.OrderBy(SignalDepth).ToArray())
            {
                if (!ReferenceEquals(element.Surface, this) || covered.Contains(element))
                {
                    continue;
                }

                Compose.Definitions.Widget previous = element.Widget;
                Compose.Definitions.Widget? next;
                _preparingVirtualDeclaration = true;
                try
                {
                    next = element.Signals?.ConstructionChanged == true && previous.Construction is { } construction
                        ? CaptureBuild(construction) : previous.Recreate(new(element));
                }
                finally
                {
                    _preparingVirtualDeclaration = false;
                }

                if (next is null)
                {
                    Func<Compose.Definitions.Widget>? build = _rootElement.Widget.Construction;
                    if (build is null)
                    {
                        UiDeclarationRecipe recipe = UiDeclarationRecipe.Capture(_rootElement)
                            ?? throw new InvalidOperationException("Reactive handwritten components require a rebuild hook or construction recipe.");
                        build = recipe.Build;
                    }

                    _pendingViewportBuild = build;
                    _pendingRebuild = CaptureBuild(build);
                    _forceViewportRebuild = true;
                    _pendingViewportElements.Clear();
                    UpdateLayout();
                    return true;
                }

                next.Construction ??= previous.Construction;
                next.ConstructionReads ??= previous.ConstructionReads;
                ValidateRoot(next);
                RebuildPlan plan;
                _preparingVirtualDeclaration = true;
                _applyingRebuild = true;
                try
                {
                    plan = new(element, next, true, _rootElement, _treeValidators, _sharedElements, PrepareAnimationUpdate);
                }
                finally
                {
                    _preparingVirtualDeclaration = false;
                    _applyingRebuild = false;
                }

                plans.Add(plan);
                covered.UnionWith(plan.Updates.Select(update => update.Element));
                covered.UnionWith(plan.Removed);
            }

            if (plans.Count == 0)
            {
                _pendingViewportElements.Clear();
                return false;
            }

            using var combined = new RebuildPlan(_rootElement, plans, _treeValidators, _sharedElements);
            UpdateVirtualLayout(combined);
            return true;
        }
        catch (Exception error)
        {
            FailSignalUpdate(error);
            ThrowSignalFailure();
            throw;
        }
        finally
        {
            foreach (RebuildPlan plan in plans)
            {
                plan.Dispose();
            }
        }
    }

    private void UpdateVirtualLayout(RebuildPlan? initial = null, Compose.Definitions.Widget? candidate = null)
    {
        if (_viewportPreparationDepth >= 64)
        {
            throw new InvalidOperationException("Viewport callbacks exceeded the bounded owner update depth.");
        }

        bool inputGeometryChanged = _rootElement.IsLayoutDirty || initial is not null;
        _viewportPreparationDepth++;
        var ownedPlans = new List<RebuildPlan>();
        var touched = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
        RebuildPlan? combined = initial;
        bool committed = false;
        IsPreparingViewportLayout = true;
        _viewportProjection = initial?.Projection() ?? new(_rootElement, []);
        _viewportPreparedContent = initial is null ? new(ReferenceEqualityComparer.Instance)
            : ViewportContentState(initial.Updates);

        try
        {
            for (int iteration = 0; iteration < 64; iteration++)
            {
                UiProposedTree projection = _viewportProjection;
                var nodes = new List<UiElement>();
                var pending = new System.Collections.Generic.Stack<UiElement>();
                pending.Push(combined?.Root ?? _rootElement);
                while (pending.TryPop(out UiElement? element))
                {
                    nodes.Add(element);
                    touched.Add(element);
                    element.SetLayoutPreparation(this, projection.Configuration(element), projection.Parent(element), projection.Slots(element));
                    foreach (UiElement child in projection.Children(element).Reverse())
                    {
                        pending.Push(child);
                    }
                }

                foreach (UiElementContent content in _viewportPreparedContent.Values)
                {
                    content.PrepareLayout?.Invoke();
                }

                _layoutPass = new UiLayoutPass();
                _layoutPass.Compute(combined?.Root ?? _rootElement, UiBoxConstraints.Tight(_viewport));
                _layoutPass = null;
                var proposals = new List<RebuildPlan>();
                _viewportContentPlans = proposals;
                try
                {
                    var proposedOwners = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
                    foreach (UiElement element in nodes)
                    {
                        bool superseded = false;
                        for (UiElement? owner = element; owner is not null; owner = projection.Parent(owner))
                        {
                            if (proposedOwners.Contains(owner))
                            {
                                superseded = true;
                                break;
                            }
                        }

                        if (superseded)
                        {
                            // A parent's proposed content is not this pass's topology yet.
                            // Its descendants must use the next projected declarations, never
                            // prepare an old factory/model over that parent's newer proposal.
                            continue;
                        }

                        int previous = proposals.Count;
                        element.PrepareViewportContent();
                        for (int index = previous; index < proposals.Count; index++)
                        {
                            foreach (ElementUpdate update in proposals[index].Updates)
                            {
                                if (update.Content is not null)
                                {
                                    proposedOwners.Add(update.Element);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    ownedPlans.AddRange(proposals);
                    _viewportContentPlans = null;
                }

                if (proposals.Count == 0)
                {
                    if (combined is not null)
                    {
                        var reachable = new HashSet<UiElement>(nodes, ReferenceEqualityComparer.Instance);
                        combined.Updates.RemoveAll(update => !reachable.Contains(update.Element));
                        combined.RetainReachable(reachable);
                        PrepareAnimationUpdates(combined);
                        combined.Prepare();
                        PrepareSignalUpdates(combined);
                        var retiring = combined.Removed.Where(element => element.IsMounted && element.HasPendingInputLifetime).ToList();
                        if (retiring.Count != 0)
                        {
                            _viewportRetirement = retiring;
                            if (_focusedElement is { } focused && combined.Removed.Contains(focused))
                            {
                                _viewportDeferredFocus = focused;
                                _viewportDeferredFocusRequest = _focusRequest;
                            }

                            CancelChangedInput(combined);
                            if (retiring.Any(element => element.HasPendingInputLifetime))
                            {
                                return;
                            }
                        }
                    }

                    IsPreparingViewportLayout = false;
                    foreach (UiElement element in touched)
                    {
                        element.CompletePreparedLayout(true);
                    }

                    var notifications = new List<Action>();
                    foreach (ScrollViewElement scroll in nodes.OfType<ScrollViewElement>())
                    {
                        if (scroll.PreparationController.CommitProposedGeometry() is { } notification)
                        {
                            notifications.Add(notification);
                        }
                    }

                    foreach (UiElement element in nodes)
                    {
                        element.AcceptViewportPreparation();
                        if (element.TakeViewportNotification() is { } notification)
                        {
                            notifications.Add(notification);
                        }
                    }

                    committed = true;
                    _pendingViewportElements.Clear();
                    _viewportRetirement.Clear();
                    _viewportDeferredFocus = null;
                    List<Exception>? failures = null;
                    if (combined is not null)
                    {
                        _applyingRebuild = true;
                        try
                        {
                            if (ReferenceEquals(_pendingRebuild, candidate))
                            {
                                _pendingRebuild = null;
                                _pendingViewportBuild = null;
                                _forceViewportRebuild = false;
                            }

                            Publish(combined, drainAdoption: true);
                        }
                        catch (Exception error)
                        {
                            (failures ??= []).Add(error);
                        }
                        finally
                        {
                            _applyingRebuild = false;
                        }
                    }

                    try
                    {
                        if (inputGeometryChanged || combined is not null)
                        {
                            InvalidateInput();
                        }

                        UpdateInput();
                        CompleteVirtualPresentation();
                    }
                    catch (Exception error)
                    {
                        (failures ??= []).Add(error);
                    }

                    foreach (Action notification in notifications)
                    {
                        try
                        {
                            notification();
                        }
                        catch (Exception error)
                        {
                            (failures ??= []).Add(error);
                        }
                    }

                    try
                    {
                        RevealPendingFocus();
                    }
                    catch (Exception error)
                    {
                        (failures ??= []).Add(error);
                    }

                    ThrowFailures(failures);
                    if (!_isDisposed && _rootElement.IsLayoutDirty)
                    {
                        UpdateVirtualLayout();
                    }

                    return;
                }

                RebuildPlan[] sources = combined is null ? proposals.ToArray() : new[] { combined }.Concat(proposals).ToArray();
                combined = new(_rootElement, sources, _treeValidators, _sharedElements);
                ownedPlans.Add(combined);
                _viewportProjection = combined.Projection();
                foreach (ElementUpdate update in proposals.SelectMany(plan => plan.Updates))
                {
                    for (UiElement? changed = update.Element; changed is not null; changed = _viewportProjection.Parent(changed))
                    {
                        changed.InvalidatePreparedLayout();
                    }
                }

                _viewportPreparedContent = ViewportContentState(combined.Updates);
            }

            throw new InvalidOperationException("Viewport content preparation exceeded its bounded convergence limit.");
        }
        finally
        {
            _layoutPass = null;
            IsPreparingViewportLayout = false;
            _viewportProjection = null;
            _viewportContentPlans = null;
            _viewportPreparedContent.Clear();
            foreach (UiElement element in touched)
            {
                if (!committed)
                {
                    element.CompletePreparedLayout(false);
                    element.DiscardViewportPreparation();
                    if (element is ScrollViewElement scroll)
                    {
                        scroll.PreparationController.DiscardProposedGeometry();
                    }
                }

                element.SetLayoutPreparation(null);
            }

            foreach (RebuildPlan plan in ownedPlans)
            {
                plan.Dispose();
            }

            if (!committed)
            {
                _rootElement.MarkLayoutDirty();
            }

            _viewportPreparationDepth--;
        }
    }
}
