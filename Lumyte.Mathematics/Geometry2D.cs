using System.Numerics;

namespace Lumyte.Mathematics;

public static class Geometry2D
{
    public static Vector2 ClosestPointOnSegment(
        Vector2 point,
        Vector2 start,
        Vector2 end,
        float degenerateLengthSquared = 1e-9f)
    {
        if (degenerateLengthSquared < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(degenerateLengthSquared),
                "Degenerate length threshold cannot be negative.");
        }

        Vector2 segment = end - start;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared <= degenerateLengthSquared)
        {
            return start;
        }

        float progress = Math.Clamp(
            Vector2.Dot(point - start, segment) / lengthSquared,
            0f,
            1f);
        return start + (segment * progress);
    }

    public static float DistancePointToSegment(
        Vector2 point,
        Vector2 start,
        Vector2 end,
        float degenerateLengthSquared = 1e-9f) =>
        Vector2.Distance(
            point,
            ClosestPointOnSegment(point, start, end, degenerateLengthSquared));
}
