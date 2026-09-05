namespace Lumyte.Resources;

/// <summary>Reports that an asset source failed while opening or reading data.</summary>
public sealed class AssetSourceException(
    string message,
    Exception? innerException = null)
    : ResourceException(message, innerException);
