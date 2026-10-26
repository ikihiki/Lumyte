using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable sweep paint node.</summary>
/// <param name="Center">The Center value.</param>
/// <param name="StartAngle">The StartAngle value.</param>
/// <param name="EndAngle">The EndAngle value.</param>
/// <param name="Stops">The Stops value.</param>
/// <param name="Extend">The Extend value.</param>
public sealed record SweepPaintNode2D(Vector2 Center, float StartAngle, float EndAngle, IReadOnlyList<ColorStop2D> Stops, ExtendMode2D Extend) : PaintNode2D;
