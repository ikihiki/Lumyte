using Xunit;

namespace Lumyte.Maps.Tests;

/// <summary>Checks XYZ and double-precision Mercator boundaries without GPU or network.</summary>
public sealed class CoordinatesTests
{
    /// <summary>Dateline and repeated worlds normalize to one acquisition identity.</summary>
    [Fact]
    public void DatelineAndWorldRepetitionsNormalize()
    {
        Assert.Equal(-180, new MapCamera(180, 0, 3).Normalize(0, 22).Longitude);
        Assert.Equal(-180, new MapCamera(-540, 0, 3).Normalize(0, 22).Longitude);
        Assert.Equal(new MapTileId(3, 7, 0), new MapTileId(3, -1, 0).Normalize());
        Assert.Equal(new MapTileId(3, 0, 7), new MapTileId(3, 16, 7).Normalize());
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapTileId(3, 0, 8).Normalize());
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapTileId(23, 0, 0).Normalize());
    }

    /// <summary>Finite validation precedes projection and zoom/latitude clamping.</summary>
    [Fact]
    public void MercatorLimitsAndFiniteInputsAreDeterministic()
    {
        MapCamera north = new MapCamera(0, 90, 24).Normalize(2, 20);
        Assert.Equal(MapCamera.LatitudeLimit, north.Latitude);
        Assert.Equal(20, north.Zoom);
        Assert.InRange(north.Project().Y, 0, 1e-14);
        Assert.InRange(new MapCamera(0, -90, 0).Project().Y, 1 - 1e-14, 1);
        Assert.Equal((0.5, 0.5), new MapCamera(0, 0, 0).Project());
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapCamera(double.NaN, 0, 0).Normalize(0, 22));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapCamera(0, double.PositiveInfinity, 0).Normalize(0, 22));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapCamera(0, 0, double.NegativeInfinity).Normalize(0, 22));
    }
}
