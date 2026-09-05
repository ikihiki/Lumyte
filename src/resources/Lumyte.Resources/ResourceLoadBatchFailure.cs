namespace Lumyte.Resources;

public sealed record ResourceLoadBatchFailure(
    string Key,
    Type ResourceType,
    Exception Exception);
