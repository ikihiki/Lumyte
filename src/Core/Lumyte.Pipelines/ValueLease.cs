namespace Lumyte.Pipelines;

/// <summary>A scope-validated borrowed immutable value.</summary>
/// <typeparam name="TV">The TV port payload.</typeparam>
public sealed class ValueLease<TV> : IValueLeaseMetadata
{
    internal ValueLease(TV value, object run, object producer)
    {
        Value = value;
        Run = run;
        Producer = producer;
    }

    /// <inheritdoc/>
    IHostPlacement? IValueLeaseMetadata.HostPlacement => Value is IHostPlacement host ? host : null;

    /// <inheritdoc/>
    SchemaExtent? IValueLeaseMetadata.PlacementExtent => Value switch
    {
        IHostPlacement host => host.Extent,
        INonCpuReadablePlacement device => device.Extent,
        IImmutablePipelineInput input => input.Description,
        _ => null,
    };

    /// <inheritdoc/>
    object? IValueLeaseMetadata.UntypedValue => Value;

    /// <inheritdoc/>
    SchemaDescriptor? IValueLeaseMetadata.PlacementSchema => Value switch
    {
        IHostPlacement host => host.Descriptor,
        INonCpuReadablePlacement extension => extension.Descriptor,
        _ => null,
    };

    internal TV Value { get; }

    internal object Run { get; }

    internal object Producer { get; }
}
