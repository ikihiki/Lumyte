namespace Lumyte.Maps;

/// <summary>A signed tile-space point; buffer coordinates remain unclipped.</summary>
/// <param name="X">Horizontal coordinate.</param>
/// <param name="Y">Downward vertical coordinate.</param>
public readonly record struct MapPoint(int X, int Y);
