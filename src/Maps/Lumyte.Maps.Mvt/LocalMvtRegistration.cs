using System.Globalization;
using Lumyte.Resources;

namespace Lumyte.Maps.Mvt;

/// <summary>Immutable local tile registration shared by the source and decode stage.</summary>
public sealed class LocalMvtRegistration
{
    private readonly IReadOnlyDictionary<ResourceId, string> _files;

    /// <summary>Initializes a new instance of the <see cref="LocalMvtRegistration"/> class with explicit local filenames.</summary>
    /// <param name="dataSetId">Data set identity.</param>
    /// <param name="contentVersion">Immutable application content version.</param>
    /// <param name="profile">Shared input and decode profile.</param>
    /// <param name="files">Normalized XYZ IDs and local filenames.</param>
    public LocalMvtRegistration(string dataSetId, string contentVersion, MapTileLoadProfile profile, IReadOnlyDictionary<MapTileId, string> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentVersion);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(files);
        DataSetId = dataSetId;
        ContentVersion = contentVersion;
        Profile = profile;
        Prefix = $"mvt/{Uri.EscapeDataString(dataSetId)}/{Uri.EscapeDataString(contentVersion)}/{profile.Fingerprint}";
        Variant = $"mvt2-bounded-v1/{MvtDecoder.SchemaId}/{profile.Fingerprint}";
        var copied = new Dictionary<ResourceId, string>();
        foreach (KeyValuePair<MapTileId, string> pair in files)
        {
            copied.Add(GetKey(pair.Key).Address.Id, Path.GetFullPath(pair.Value));
        }

        _files = copied;
    }

    /// <summary>Gets the immutable dataset identity.</summary>
    public string DataSetId { get; }

    /// <summary>Gets the immutable content version.</summary>
    public string ContentVersion { get; }

    /// <summary>Gets the immutable profile.</summary>
    public MapTileLoadProfile Profile { get; }

    /// <summary>Gets the content and profile identity prefix.</summary>
    public string Prefix { get; }

    /// <summary>Gets the decoder, schema and profile variant.</summary>
    public string Variant { get; }

    /// <summary>Creates a stable normalized tile key.</summary>
    /// <param name="tile">Tile ID, with wrapped X permitted.</param>
    /// <returns>The shared CPU key.</returns>
    public ResourceKey<MapTileContent> GetKey(MapTileId tile)
    {
        MapTileId id = tile.Normalize();
        return new ResourceKey<MapTileContent>(string.Create(CultureInfo.InvariantCulture, $"{Prefix}/{id.Zoom}/{id.X}/{id.Y}"), variant: Variant);
    }

    internal string Resolve(ResourceId id) => _files.TryGetValue(id, out string? path) ? path : throw new FileNotFoundException("Tile is not in this local registration.");

    internal void Validate(ResourceAddress address)
    {
        Resolve(address.Id);
        if (address.Variant != Variant || address.Subresource.Length != 0)
        {
            throw new InvalidOperationException("Tile address does not match its registered profile and decoder.");
        }
    }
}
