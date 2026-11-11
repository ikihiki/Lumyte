using System.Numerics;

namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private Vector2 _viewport;
    private UiLayoutPass? _layoutPass;

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
        UpdateTree();
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
}
