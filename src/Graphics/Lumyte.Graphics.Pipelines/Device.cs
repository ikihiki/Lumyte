using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>A device placement with dependency readiness, never a CPU-readable payload.</summary>
/// <typeparam name="TSchema">The canonical schema.</typeparam>
public readonly struct Device<TSchema> : INonCpuReadablePlacement, IGraphicsDeviceValue
    where TSchema : ISchema
{
    internal Device(DeviceValue value)
    {
        Value = value;
    }

    /// <inheritdoc/>
    SchemaDescriptor INonCpuReadablePlacement.Descriptor => Value?.Descriptor ?? TSchema.Descriptor;

    /// <inheritdoc/>
    SchemaExtent? INonCpuReadablePlacement.Extent => Value?.Extent;

    /// <inheritdoc/>
    DeviceValue? IGraphicsDeviceValue.Value => Value;

    /// <inheritdoc/>
    string INonCpuReadablePlacement.DiagnosticPlacement => "Device";

    internal DeviceValue? Value { get; }
}
