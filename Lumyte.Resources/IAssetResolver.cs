namespace Lumyte.Resources;

/// <summary>Maps one asset address scheme to a physical asset location.</summary>
public interface IAssetResolver
{
    string Scheme { get; }

    ValueTask<AssetLocation> ResolveAsync(
        AssetAddress address,
        CancellationToken cancellationToken = default);
}
