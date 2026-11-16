using Lumyte.Resources;

namespace Lumyte.Maps;

/// <summary>An immutable schema and content-version boundary for normalized XYZ resources.</summary>
public abstract class MapTileSet
{
    /// <summary>Initializes a new instance of the <see cref="MapTileSet"/> class.</summary>
    /// <param name="id">Dataset identity.</param>
    /// <param name="contentVersion">Immutable dataset version.</param>
    /// <param name="schemaId">Exact schema identity.</param>
    /// <param name="minZoom">Minimum zoom.</param>
    /// <param name="maxZoom">Maximum zoom.</param>
    /// <param name="attribution">Required attribution text.</param>
    /// <param name="loadProfile">Registered immutable load limits.</param>
    protected MapTileSet(string id, string contentVersion, string schemaId, int minZoom, int maxZoom, string attribution, MapTileLoadProfile loadProfile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(attribution);
        ArgumentNullException.ThrowIfNull(loadProfile);
        if (minZoom < 0 || maxZoom > 22 || minZoom > maxZoom)
        {
            throw new ArgumentOutOfRangeException(nameof(minZoom));
        }

        Id = id;
        ContentVersion = contentVersion;
        SchemaId = schemaId;
        MinZoom = minZoom;
        MaxZoom = maxZoom;
        Attribution = attribution;
        LoadProfile = loadProfile;
    }

    /// <summary>Gets dataset identity.</summary>
    public string Id { get; }

    /// <summary>Gets immutable content version.</summary>
    public string ContentVersion { get; }

    /// <summary>Gets schema identity.</summary>
    public string SchemaId { get; }

    /// <summary>Gets minimum zoom.</summary>
    public int MinZoom { get; }

    /// <summary>Gets maximum zoom.</summary>
    public int MaxZoom { get; }

    /// <summary>Gets required attribution.</summary>
    public string Attribution { get; }

    /// <summary>Gets registered immutable load limits.</summary>
    public MapTileLoadProfile LoadProfile { get; }

    /// <summary>Resolves only normalized in-range XYZ identifiers.</summary>
    /// <param name="id">Normalized tile.</param>
    /// <returns>The profile and content-version specific key.</returns>
    public abstract ResourceKey<MapTileContent> GetKey(MapTileId id);
}
