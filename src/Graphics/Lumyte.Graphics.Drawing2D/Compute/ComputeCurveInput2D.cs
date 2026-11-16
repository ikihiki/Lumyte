using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>Raw logical control points; P0 is start, P1/P2 controls, P3 end (quadratic ends at P2).
/// Move has zero output edges and retains isolated points. ContourIndex preserves topology.
/// GPU writes at EdgeOffset through EdgeOffset + SubdivisionCount exclusively.</summary>
internal readonly record struct ComputeCurveInput2D(
    PathVerb2D Verb,
    Vector2 P0,
    Vector2 P1,
    Vector2 P2,
    Vector2 P3,
    int ContourIndex,
    int EdgeOffset,
    int SubdivisionCount,
    bool ImplicitClose);
