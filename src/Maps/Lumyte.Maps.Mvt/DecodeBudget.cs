namespace Lumyte.Maps.Mvt;

internal sealed class DecodeBudget
{
    private readonly MapTileLoadProfile _profile;
    private readonly int _inputBytes;
    private long _attributes;
    private long _features;
    private long _vertices;

    internal DecodeBudget(MapTileLoadProfile profile, int inputBytes)
    {
        _profile = profile;
        _inputBytes = inputBytes;
        if (inputBytes > profile.MaxInputBytes || inputBytes > profile.MaxReservationBytesPerTile)
        {
            throw new InvalidDataException("Input capacity exceeded.");
        }
    }

    internal long Total { get; private set; }

    internal long Retained { get; private set; }

    internal void Charge(long bytes, bool retained = true)
    {
        if (bytes < 0 || bytes > _profile.MaxDecodedBytes - Total || bytes > _profile.MaxReservationBytesPerTile - _inputBytes - Total)
        {
            throw new InvalidDataException("Decoded allocation capacity exceeded.");
        }

        Total += bytes;
        if (retained)
        {
            Retained += bytes;
        }
    }

    internal T[] Array<T>(int count, int elementBytes, bool retained = true)
    {
        Charge((24L + ((long)count * elementBytes) + 7) & ~7L, retained);
        return new T[count];
    }

    internal void Attributes(int count)
    {
        _attributes += count;
        if (_attributes > _profile.MaxAttributeBytes)
        {
            throw new InvalidDataException("Attribute capacity exceeded.");
        }
    }

    internal void Features(int count)
    {
        _features += count;
        if (_features > _profile.MaxFeatures)
        {
            throw new InvalidDataException("Feature capacity exceeded.");
        }
    }

    internal void Vertices(int count)
    {
        _vertices += count;
        if (_vertices > _profile.MaxVertices)
        {
            throw new InvalidDataException("Vertex capacity exceeded.");
        }
    }
}
