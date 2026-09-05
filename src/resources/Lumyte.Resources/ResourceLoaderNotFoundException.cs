namespace Lumyte.Resources;

/// <summary>Reports that no registered loader can produce the requested resource type.</summary>
public sealed class ResourceLoaderNotFoundException(
    string message,
    Exception? innerException = null)
    : ResourceException(message, innerException);
