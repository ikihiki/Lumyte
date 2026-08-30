namespace Lumyte.Resources;

/// <summary>Opens asset data for one address scheme.</summary>
public interface IAssetResolver
{
    string Scheme { get; }

    ValueTask<AssetData> OpenAsync(
        AssetAddress address,
        CancellationToken cancellationToken = default);
}
