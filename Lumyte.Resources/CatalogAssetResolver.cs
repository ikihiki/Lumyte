namespace Lumyte.Resources;

/// <summary>Resolves stable asset identifiers through a supplied catalog.</summary>
public sealed class CatalogAssetResolver : IAssetResolver
{
    private readonly IReadOnlyDictionary<string, Func<CancellationToken, ValueTask<AssetData>>> entries;

    public CatalogAssetResolver(
        IReadOnlyDictionary<string, Func<CancellationToken, ValueTask<AssetData>>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        this.entries = entries;
    }

    public string Scheme => "asset";

    public ValueTask<AssetData> OpenAsync(
        AssetAddress address,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string id = Uri.UnescapeDataString(address.ToString());
        if (!entries.TryGetValue(
            id,
            out Func<CancellationToken, ValueTask<AssetData>>? open))
        {
            throw new AssetResolutionException(
                $"The asset catalog does not contain the '{id}' identifier.");
        }

        return open(cancellationToken);
    }
}
