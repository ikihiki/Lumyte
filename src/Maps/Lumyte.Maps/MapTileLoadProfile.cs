using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lumyte.Maps;

/// <summary>Immutable limits forming part of the shared resource identity.</summary>
public sealed class MapTileLoadProfile
{
    /// <summary>Initializes a new instance of the <see cref="MapTileLoadProfile"/> class.</summary>
    /// <param name="id">Application profile identifier.</param>
    /// <param name="maxInputBytes">Maximum uncompressed local input.</param>
    /// <param name="maxDecodedBytes">Maximum cumulative decoder allocations.</param>
    /// <param name="maxAttributeBytes">Maximum UTF8 attribute bytes.</param>
    /// <param name="maxFeatures">Maximum features.</param>
    /// <param name="maxVertices">Maximum vertices.</param>
    /// <param name="maxReservationBytesPerTile">Maximum input plus decoder allocation reservation.</param>
    public MapTileLoadProfile(string id, int maxInputBytes = 4194304, int maxDecodedBytes = 16777216, int maxAttributeBytes = 2097152, int maxFeatures = 100000, int maxVertices = 1000000, long maxReservationBytesPerTile = 33554432)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (maxInputBytes <= 0 || maxDecodedBytes <= 0 || maxAttributeBytes <= 0 || maxFeatures <= 0 || maxVertices <= 0 || maxReservationBytesPerTile <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInputBytes));
        }

        Id = id;
        MaxInputBytes = maxInputBytes;
        MaxDecodedBytes = maxDecodedBytes;
        MaxAttributeBytes = maxAttributeBytes;
        MaxFeatures = maxFeatures;
        MaxVertices = maxVertices;
        MaxReservationBytesPerTile = maxReservationBytesPerTile;
        string identity = string.Create(CultureInfo.InvariantCulture, $"{id.Length}:{id}|{maxInputBytes}|{maxDecodedBytes}|{maxAttributeBytes}|{maxFeatures}|{maxVertices}|{maxReservationBytesPerTile}");
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    /// <summary>Gets the application identity.</summary>
    public string Id { get; }

    /// <summary>Gets the complete deterministic limits identity.</summary>
    public string Fingerprint { get; }

    /// <summary>Gets the local input limit.</summary>
    public int MaxInputBytes { get; }

    /// <summary>Gets the decoder cumulative allocation limit.</summary>
    public int MaxDecodedBytes { get; }

    /// <summary>Gets the UTF8 attribute budget.</summary>
    public int MaxAttributeBytes { get; }

    /// <summary>Gets the feature limit.</summary>
    public int MaxFeatures { get; }

    /// <summary>Gets the vertex limit.</summary>
    public int MaxVertices { get; }

    /// <summary>Gets the combined reservation limit.</summary>
    public long MaxReservationBytesPerTile { get; }
}
