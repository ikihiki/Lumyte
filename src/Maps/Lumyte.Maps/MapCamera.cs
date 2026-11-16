namespace Lumyte.Maps;

/// <summary>A rotation-free Web Mercator camera.</summary>
/// <param name="Longitude">Longitude in degrees.</param>
/// <param name="Latitude">Latitude in degrees.</param>
/// <param name="Zoom">Continuous zoom.</param>
public readonly record struct MapCamera(double Longitude, double Latitude, double Zoom)
{
    /// <summary>The Mercator latitude limit.</summary>
    public const double LatitudeLimit = 85.0511287798066;

    /// <summary>Normalizes finite coordinates.</summary>
    /// <param name="minZoom">Minimum zoom.</param>
    /// <param name="maxZoom">Maximum zoom.</param>
    /// <returns>The normalized camera.</returns>
    public MapCamera Normalize(int minZoom, int maxZoom)
    {
        if (!double.IsFinite(Longitude) || !double.IsFinite(Latitude) || !double.IsFinite(Zoom)
            || minZoom < 0 || maxZoom > 22 || minZoom > maxZoom)
        {
            throw new ArgumentOutOfRangeException(nameof(minZoom));
        }

        double longitude = Longitude % 360;
        longitude = longitude < -180 ? longitude + 360 : longitude >= 180 ? longitude - 360 : longitude;
        return new(longitude, Math.Clamp(Latitude, -LatitudeLimit, LatitudeLimit), Math.Clamp(Zoom, minZoom, maxZoom));
    }

    /// <summary>Projects to normalized double precision world coordinates.</summary>
    /// <returns>World coordinates, Y increasing southward.</returns>
    public (double X, double Y) Project()
    {
        MapCamera normalized = Normalize(0, 22);
        double radians = normalized.Latitude * Math.PI / 180;
        return ((normalized.Longitude + 180) / 360, Math.Clamp((1 - (Math.Asinh(Math.Tan(radians)) / Math.PI)) / 2, 0, 1));
    }
}
