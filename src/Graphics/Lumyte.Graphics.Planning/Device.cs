using Lumyte.Pipelines;

namespace Lumyte.Graphics.Planning;

/// <summary>An immutable resource declaration, never CPU-readable GPU contents.</summary>
/// <typeparam name = "TSchema">The canonical schema.</typeparam>
public readonly struct Device<TSchema> : INonCpuReadablePlacement, IPlanningDeviceValue
    where TSchema : ISchema
{
    private readonly PlanResource? _resource;

    internal Device(PlanResource resource, SchemaExtent extent)
    {
        _resource = resource;
        Extent = extent;
    }

    /// <inheritdoc/>
    public SchemaDescriptor Descriptor => TSchema.Descriptor;

    /// <inheritdoc/>
    public SchemaExtent? Extent { get; }

    /// <inheritdoc/>
    public string DiagnosticPlacement => "GraphicsPlan";

    /// <inheritdoc/>
    PlanResource IPlanningDeviceValue.Resource => Resource;

    internal PlanResource Resource => _resource ?? throw new InvalidOperationException("Uninitialized device declaration.");
}
