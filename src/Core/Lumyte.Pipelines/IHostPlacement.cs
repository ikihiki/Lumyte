namespace Lumyte.Pipelines;

/// <summary>Exposes canonical host metadata without reflection or domain coupling.</summary>
internal interface IHostPlacement
{
    /// <summary>Gets the canonical descriptor.</summary>
    SchemaDescriptor Descriptor { get; }

    /// <summary>Gets the initialized extent, or null for a default placement.</summary>
    SchemaExtent? Extent { get; }

    /// <summary>Gets the publication invocation, or null for a default placement.</summary>
    object? Invocation { get; }
}
