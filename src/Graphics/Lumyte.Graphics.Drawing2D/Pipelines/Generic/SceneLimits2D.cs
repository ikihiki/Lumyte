namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Finite aggregate expansion ceilings. Conservative admission may reject before the exact ceiling.</summary>
public sealed record SceneLimits2D(int MaxContours, int MaxSegments, int MaxStrokeTriangles, int MaxTileReferences)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxContours);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxSegments);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxStrokeTriangles);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxTileReferences);
    }
}
