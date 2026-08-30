namespace Lumyte.Resources;

/// <summary>Reports that a resource loader failed to construct a resource.</summary>
public sealed class ResourceLoadException(
    string message,
    Exception? innerException = null)
    : ResourceException(message, innerException);
