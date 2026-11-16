using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;

namespace Lumyte.UI.Application.Browser.Sample;

internal sealed class PathProbe : Lumyte.UI.Compose.Definitions.Panel
{
    protected override UiElement CreateElement() => new PathProbeElement(this);

    private sealed class PathProbeElement(PathProbe widget) : PanelElement(widget)
    {
        protected override void OnRecord(DrawingCanvas2D canvas)
        {
            var path = new PathBuilder2D();
            path.MoveTo(new(2, 2));
            path.LineTo(new(20, 2));
            path.LineTo(new(10, 20));
            path.Close();
            canvas.FillPath(path.Build(), new SRgba<float>(0.5f, 0.5f, 0.5f, 1));
        }
    }
}
