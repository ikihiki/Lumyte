namespace Lumyte.Pipelines;

/// <summary>Exposes immutable runtime input metadata to preflight.</summary>
internal interface IValueLeaseMetadata
{
    /// <summary>Gets the canonical host placement, when present.</summary>
    IHostPlacement? HostPlacement { get; }

    /// <summary>Gets canonical placement metadata, when present.</summary>
    SchemaExtent? PlacementExtent { get; }

    /// <summary>Gets the immutable value for a trusted extension preflight.</summary>
    object? UntypedValue { get; }

    /// <summary>Gets the actual captured canonical placement schema, when present.</summary>
    SchemaDescriptor? PlacementSchema { get; }
}
