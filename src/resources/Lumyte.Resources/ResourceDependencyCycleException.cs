namespace Lumyte.Resources;

/// <summary>Reports a cycle in the resource dependency graph.</summary>
public sealed class ResourceDependencyCycleException(
    string message,
    Exception? innerException = null)
    : ResourceException(message, innerException);
