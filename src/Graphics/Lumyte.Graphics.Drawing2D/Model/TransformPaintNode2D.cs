using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable transform paint node.</summary>
/// <param name="Transform">The Transform value.</param>
/// <param name="Child">The Child value.</param>
public sealed record TransformPaintNode2D(Matrix3x2 Transform, PaintNode2D Child) : PaintNode2D;
