namespace Lumyte.Resources;

/// <summary>Reports that a requested resource is absent from an asset.</summary>
public sealed class ResourceNotFoundException(
    string message,
    Exception? innerException = null)
    : ResourceException(message, innerException);
