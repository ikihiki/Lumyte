namespace Lumyte.Resources;

public interface IAssetChangeSource
{
    event Action<AssetChange>? Changed;
}
