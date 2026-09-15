using System.Numerics;

namespace Lumyte.Graphics.TwoD;

public readonly record struct PathSegment(
    PathSegmentKind Kind,
    Vector2 Point,
    Vector2 Control0 = default,
    Vector2 Control1 = default);
