namespace Lumyte.Resources;

/// <summary>Provides the stable environment required by resource stages.</summary>
/// <remarks>
/// The manager captures the executor and collects dependencies once during registration.
/// Device services remain stable across physical epoch changes; acquire their current leases per build.
/// The host owns shared services; the manager retains their uses until output cleanup completes.
/// </remarks>
public interface IResourceRuntime
{
    /// <summary>Gets the shared executor used to construct and release outputs in this environment.</summary>
    IResourceExecutor Executor { get; }

    /// <summary>Gets the environment dependencies observed by the manager, collected once during registration.</summary>
    IReadOnlyList<IResourceRuntimeDependency> Dependencies => [];
}
