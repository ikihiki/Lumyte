namespace Lumyte.Resources;

public sealed record ResourceHotReloadFailure(
    AssetChange Change,
    Exception Exception);
