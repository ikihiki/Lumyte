namespace Lumyte.Maps;

/// <summary>Supported CPU geometry kinds.</summary>
public enum MapGeometryType
{
    /// <summary>Open paths.</summary>
    LineString = 2,

    /// <summary>Closed rings; positive screen-coordinate winding is exterior.</summary>
    Polygon = 3,
}
