namespace Lumyte.Resources;

/// <summary>Resolves stable asset identifiers through a supplied catalog.</summary>
public sealed class CatalogAssetResolver : IAssetResolver
{
    private readonly IReadOnlyDictionary<string, AssetLocation> entries;

    public CatalogAssetResolver(IReadOnlyDictionary<string, AssetLocation> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        this.entries = entries;
    }

    public string Scheme => "asset";

    public ValueTask<AssetLocation> ResolveAsync(
        AssetAddress address,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string id = Uri.UnescapeDataString(address.ToString());
        if (!entries.TryGetValue(id, out AssetLocation location))
        {
            throw new AssetResolutionException(
                $"The asset catalog does not contain the '{id}' identifier.");
        }

        return ValueTask.FromResult(location);
    }
}
