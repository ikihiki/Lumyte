using System.Numerics;

namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private Vector2 _viewport;
    private UiLayoutPass? _layoutPass;

    internal bool IsPreparingViewportLayout { get; private set; }

    internal UiLayoutPass? LayoutPass => _layoutPass;

    /// <summary>Sets the logical viewport and invalidates geometry when it changes.</summary>
    /// <param name="logicalSize">The finite nonnegative viewport.</param>
    public void SetViewport(Vector2 logicalSize)
    {
        ThrowIfDisposed();
        UiLayoutMath.ValidateSize(logicalSize, nameof(logicalSize));
        if (_viewport != logicalSize)
        {
            _viewport = logicalSize;
            _rootElement.MarkLayoutDirty();
        }
    }

    /// <summary>Applies pending declarations, then computes dirty geometry without rollback.</summary>
    public void UpdateLayout()
    {
        ThrowIfDisposed();
        ThrowSignalFailure();
        if (_dispatchDepth != 0 && (_forceViewportRebuild || _pendingViewportElements.Count != 0 || CollectElements(_rootElement).Any(element => element.Widget.RequiresViewportPreparation)))
        {
            return;
        }

        if (_preparingVirtualDeclaration || (_applyingRebuild && _viewportPreparationDepth != 0))
        {
            return;
        }

        if (IsPreparingViewportLayout)
        {
            throw new InvalidOperationException("Layout cannot reenter unpublished viewport preparation.");
        }

        _viewportRetirement.RemoveAll(element => !element.IsMounted || !element.HasPendingInputLifetime);
        if (_viewportRetirement.Count != 0)
        {
            UpdateInput();
            return;
        }

        if (_pendingRebuild is { } candidate && (_pendingViewportBuild is not null || _forceViewportRebuild || CollectElements(_rootElement).Any(element => element.Widget.RequiresViewportPreparation) || ContainsViewportDeclaration(candidate)))
        {
            RebuildPlan initial;
            _applyingRebuild = true;
            _preparingVirtualDeclaration = true;
            try
            {
                if (_pendingViewportBuild is { } build)
                {
                    candidate = CaptureBuild(build);
                    _pendingRebuild = candidate;
                }

                initial = new(_rootElement, candidate, validators: _treeValidators, shared: _sharedElements, prepareArguments: PrepareAnimationUpdate);
            }
            finally
            {
                _applyingRebuild = false;
                _preparingVirtualDeclaration = false;
            }

            using (initial)
            {
                UpdateVirtualLayout(initial, candidate);
            }

            return;
        }

        if (_pendingRebuild is null && _pendingViewportElements.Count != 0 && UpdatePendingViewportElements())
        {
            return;
        }

        UpdateTree();
        if (_pendingViewportElements.Count != 0 && UpdatePendingViewportElements())
        {
            return;
        }

        if (_pendingRebuild is { } pendingVirtual && (_pendingViewportBuild is not null || _forceViewportRebuild || CollectElements(_rootElement).Any(element => element.Widget.RequiresViewportPreparation) || ContainsViewportDeclaration(pendingVirtual)))
        {
            UpdateLayout();
            return;
        }

        if (CollectElements(_rootElement).Any(element => element.Widget.RequiresViewportPreparation) || CollectElements(_rootElement).OfType<ScrollViewElement>().Any(scroll => scroll.Widget.DeferredPosition))
        {
            UpdateVirtualLayout();
            return;
        }

        UiProfiling.Scope? profile = UiProfiling.Begin(2);
        try
        {
            if (_isDisposed)
            {
                profile?.Skip();
                return;
            }

            if (!_rootElement.IsLayoutDirty)
            {
                profile?.Skip();
                profile?.Dispose();
                profile = null;
                UpdateInput();
                CompleteVirtualPresentation();
                RevealPendingFocus();
                return;
            }

            var pass = new UiLayoutPass();
            _layoutPass = pass;
            try
            {
                try
                {
                    pass.Compute(_rootElement, UiBoxConstraints.Tight(_viewport));
                }
                catch (Exception error)
                {
                    _rootElement.MarkLayoutDirty();
                    profile?.Fail(error);
                    throw;
                }
                finally
                {
                    profile?.Dispose();
                    profile = null;
                }

                try
                {
                    InvalidateInput();
                    UpdateInput();
                    CompleteVirtualPresentation();
                }
                catch
                {
                    _rootElement.MarkLayoutDirty();
                    throw;
                }
            }
            finally
            {
                _layoutPass = null;
            }

            RevealPendingFocus();
        }
        catch (Exception error)
        {
            profile?.Fail(error);
            throw;
        }
        finally
        {
            profile?.Dispose();
        }
    }

    private static bool ContainsViewportDeclaration(Compose.Definitions.Widget root) => root.RequiresViewportPreparation || root.ChildCollections.SelectMany(slot => slot).Any(ContainsViewportDeclaration);
}
