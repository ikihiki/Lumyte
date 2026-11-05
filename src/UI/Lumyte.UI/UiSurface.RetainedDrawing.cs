using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    internal Vector2 DrawingViewport => _viewport;

    internal bool RequiresOpaqueDrawing => VisualHost is { RequiresComposition: true } || PageTransitioning || _visualExclusions is not null || _logicalVisualCapture;

    /// <summary>Creates an opt-in retained drawing adapter; disposing it does not dispose this surface.</summary>
    /// <returns>A caller-owned adapter, independent of the hosted drawing source.</returns>
    public UiDrawingScene2D CreateDrawingScene() => new(this);

    internal void ValidateRetainedDrawingDependencies()
    {
        UpdateTree();
        foreach (UiElement element in CollectElements(RootElement))
        {
            element.ValidateRetainedDrawingDependencies();
        }
    }

    internal DrawList2D RecordDrawingFragment(UiElement element, bool selfOnly)
    {
        ThrowIfDisposed();
        var canvas = new Canvas2D();
        canvas.Begin();
        _recordDepth++;
        try
        {
            element.RecordModified(canvas, selfOnly);
        }
        finally
        {
            _recordDepth--;
        }

        return canvas.End();
    }
}
