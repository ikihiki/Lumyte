namespace Lumyte.Resources;

/// <summary>Indicates that a required runtime generation is currently unavailable or invalidated.</summary>
/// <param name="dependencyId">The unavailable runtime dependency identifier.</param>
/// <param name="version">The required runtime version.</param>
public sealed class ResourceUnavailableException(string dependencyId, ulong version)
    : InvalidOperationException($"Runtime dependency '{dependencyId}' at version {version} is unavailable.")
{
    /// <summary>Gets the unavailable dependency identifier.</summary>
    public string DependencyId { get; } = dependencyId;

    /// <summary>Gets the required version.</summary>
    public ulong Version { get; } = version;
}
