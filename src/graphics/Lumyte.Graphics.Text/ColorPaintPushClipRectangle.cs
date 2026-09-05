using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

/// <summary>Pushes an axis-aligned rectangle onto the COLRv1 clip stack.</summary>
internal sealed record ColorPaintPushClipRectangle(Rect Rectangle) : ColorPaintOperation;
