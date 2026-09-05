using System.Numerics;

namespace Lumyte.Graphics.Text;

/// <summary>Fills the active COLRv1 clip with an angular sweep gradient.</summary>
internal sealed record ColorPaintSweepGradient(
    ColorPaintGradient Gradient,
    Vector2 Center,
    float StartAngle,
    float EndAngle) : ColorPaintOperation;
