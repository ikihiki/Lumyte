namespace Lumyte.Resources;

/// <summary>Reports that a resolved asset does not exist.</summary>
public sealed class AssetNotFoundException(
    string message,
    Exception? innerException = null)
    : ResourceException(message, innerException);
