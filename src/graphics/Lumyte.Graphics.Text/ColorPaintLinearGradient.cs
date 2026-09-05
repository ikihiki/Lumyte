using System.Numerics;

namespace Lumyte.Graphics.Text;

/// <summary>Fills the active COLRv1 clip with a linear gradient.</summary>
internal sealed record ColorPaintLinearGradient(
    ColorPaintGradient Gradient,
    Vector2 Point0,
    Vector2 Point1,
    Vector2 Point2) : ColorPaintOperation;
