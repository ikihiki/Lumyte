namespace Lumyte.Resources;

public sealed class ResourceInUseException(string message)
    : ResourceException(message);
