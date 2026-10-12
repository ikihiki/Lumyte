namespace Lumyte.Graphics.Drawing2D;

/// <summary>Records pure 2D drawing without layers, image effects or a RenderGraph dependency.</summary>
public sealed class Canvas2D : DrawingCanvas2D
{
    /// <summary>Completes an immutable pure drawing recording.</summary>
    /// <returns>The reusable command snapshot.</returns>
    public DrawList2D End() => CompleteRecording();
}
