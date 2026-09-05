using System.Numerics;

namespace Lumyte.Graphics.Text;

/// <summary>Fills the active COLRv1 clip with a two-circle radial gradient.</summary>
internal sealed record ColorPaintRadialGradient(
    ColorPaintGradient Gradient,
    Vector2 Center0,
    float Radius0,
    Vector2 Center1,
    float Radius1) : ColorPaintOperation;
