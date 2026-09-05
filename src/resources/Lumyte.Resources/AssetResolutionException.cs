namespace Lumyte.Resources;

/// <summary>Reports that no registered resolver could resolve an asset address.</summary>
public sealed class AssetResolutionException(
    string message,
    Exception? innerException = null)
    : ResourceException(message, innerException);
