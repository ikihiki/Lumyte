namespace Lumyte.Resources;

/// <summary>Exposes the state and identity of a runtime service used by resource generations.</summary>
public interface IResourceRuntimeDependency
{
    /// <summary>Notifies metadata changes; handlers must not synchronously rebuild resources.</summary>
    event EventHandler? Changed;

    /// <summary>Gets the manager-local dependency identifier.</summary>
    string Id { get; }

    /// <summary>Gets monotonic state and invalidation metadata.</summary>
    ResourceRuntimeSnapshot Snapshot { get; }

    /// <summary>Gets the exact provider identity checked when a stage borrows an external service.</summary>
    object Identity => this;
}
