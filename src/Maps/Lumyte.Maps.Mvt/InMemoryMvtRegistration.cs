using System.Security.Cryptography;
using Lumyte.Resources;

namespace Lumyte.Maps.Mvt;

/// <summary>Owns immutable, bounded copies of explicitly registered uncompressed MVT tiles. No filesystem or HTTP is used.</summary>
public sealed class InMemoryMvtRegistration
{
    private readonly LocalMvtRegistration _identity;
    private readonly Dictionary<ResourceId, Entry> _tiles = [];

    /// <summary>Initializes a new instance of the <see cref="InMemoryMvtRegistration"/> class.</summary>
    /// <param name="dataSetId">Dataset identity.</param>
    /// <param name="contentVersion">Immutable application content version.</param>
    /// <param name="profile">Shared input and decoder limits.</param>
    /// <param name="tiles">Tile bytes copied only after validating every tile's input and reservation limits.</param>
    public InMemoryMvtRegistration(string dataSetId, string contentVersion, MapTileLoadProfile profile, IReadOnlyDictionary<MapTileId, ReadOnlyMemory<byte>> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        _identity = new(dataSetId, contentVersion, profile, new Dictionary<MapTileId, string>());

        // Preflight the entire registration before taking any payload copies. Caller must
        // serialize mutation of the supplied dictionary and buffers during construction.
        var ids = new HashSet<ResourceId>();
        foreach (KeyValuePair<MapTileId, ReadOnlyMemory<byte>> pair in tiles)
        {
            if (!ids.Add(GetKey(pair.Key).Address.Id))
            {
                throw new ArgumentException("Duplicate normalized tile identity.", nameof(tiles));
            }

            ValidateLength(pair.Value.Length, profile);
        }

        foreach (KeyValuePair<MapTileId, ReadOnlyMemory<byte>> pair in tiles)
        {
            ValidateLength(pair.Value.Length, profile);
            byte[] bytes = pair.Value.ToArray();
            _tiles.Add(GetKey(pair.Key).Address.Id, new(bytes, Convert.ToHexString(SHA256.HashData(bytes))));
        }
    }

    /// <summary>Gets the immutable data set identity.</summary>
    public string DataSetId => _identity.DataSetId;

    /// <summary>Gets the immutable content version.</summary>
    public string ContentVersion => _identity.ContentVersion;

    /// <summary>Gets the shared immutable profile.</summary>
    public MapTileLoadProfile Profile => _identity.Profile;

    /// <summary>Creates a normalized CPU key with content, decoder and profile identity.</summary>
    /// <param name="tile">XYZ tile ID.</param>
    /// <returns>The registered tile key.</returns>
    public ResourceKey<MapTileContent> GetKey(MapTileId tile) => _identity.GetKey(tile);

    internal Entry Resolve(ResourceId id) => _tiles.TryGetValue(id, out Entry? entry) ? entry : throw new FileNotFoundException("Tile is not in this memory registration.");

    internal void Validate(ResourceAddress address)
    {
        Resolve(address.Id);
        if (address.Variant != _identity.Variant || address.Subresource.Length != 0)
        {
            throw new InvalidOperationException("Tile address does not match its registered profile and decoder.");
        }
    }

    private static void ValidateLength(int length, MapTileLoadProfile profile)
    {
        if (length > profile.MaxInputBytes || ((length + 31L) & ~7L) + 512 > profile.MaxReservationBytesPerTile)
        {
            throw new InvalidDataException("Memory input capacity exceeded before copying.");
        }
    }

    internal sealed record Entry(byte[] Bytes, string Revision);
}
