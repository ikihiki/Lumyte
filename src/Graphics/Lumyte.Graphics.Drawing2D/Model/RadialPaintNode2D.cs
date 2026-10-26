using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable radial paint node.</summary>
/// <param name="C0">The C0 value.</param>
/// <param name="R0">The R0 value.</param>
/// <param name="C1">The C1 value.</param>
/// <param name="R1">The R1 value.</param>
/// <param name="Stops">The Stops value.</param>
/// <param name="Extend">The Extend value.</param>
public sealed record RadialPaintNode2D(Vector2 C0, float R0, Vector2 C1, float R1, IReadOnlyList<ColorStop2D> Stops, ExtendMode2D Extend) : PaintNode2D;
