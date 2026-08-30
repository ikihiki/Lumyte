namespace Lumyte.Resources;

public readonly record struct ResourceHotReloadResult(
    AssetChange Change,
    int ReloadedResourceCount);
