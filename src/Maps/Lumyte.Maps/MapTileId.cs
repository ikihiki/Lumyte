namespace Lumyte.Maps;

/// <summary>An XYZ tile identifier.</summary>
/// <param name="Zoom">Integer zoom.</param>
/// <param name="X">Horizontal coordinate.</param>
/// <param name="Y">Vertical coordinate.</param>
public readonly record struct MapTileId(int Zoom, int X, int Y)
{
    /// <summary>Wraps X and validates nonrepeating Y.</summary>
    /// <returns>The normalized identifier.</returns>
    public MapTileId Normalize()
    {
        if (Zoom is < 0 or > 22 || Y < 0 || Y >= (1 << Zoom))
        {
            throw new ArgumentOutOfRangeException(nameof(Zoom));
        }

        int count = 1 << Zoom;
        return new(Zoom, ((X % count) + count) % count, Y);
    }
}
