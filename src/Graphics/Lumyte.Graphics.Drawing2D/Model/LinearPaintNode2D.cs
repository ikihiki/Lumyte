using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable linear paint node.</summary>
/// <param name="P0">The P0 value.</param>
/// <param name="P1">The P1 value.</param>
/// <param name="P2">The P2 value.</param>
/// <param name="Stops">The Stops value.</param>
/// <param name="Extend">The Extend value.</param>
public sealed record LinearPaintNode2D(Vector2 P0, Vector2 P1, Vector2 P2, IReadOnlyList<ColorStop2D> Stops, ExtendMode2D Extend) : PaintNode2D;
