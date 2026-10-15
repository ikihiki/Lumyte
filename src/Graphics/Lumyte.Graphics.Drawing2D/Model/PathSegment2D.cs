using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable segment: Move/Line use P0; Quadratic uses P0/P1; Cubic uses P0/P1/P2.</summary>
/// <param name="Verb">The segment operation.</param>
/// <param name="P0">The first control point or endpoint.</param>
/// <param name="P1">The second control point or endpoint.</param>
/// <param name="P2">The cubic endpoint.</param>
public readonly record struct PathSegment2D(PathVerb2D Verb, Vector2 P0, Vector2 P1, Vector2 P2);
