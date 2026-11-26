namespace Lumyte.Pipelines;

/// <summary>Describes an extension placement that cannot be read by CPU callbacks.</summary>
internal interface INonCpuReadablePlacement
{
    /// <summary>Gets the canonical descriptor.</summary>
    SchemaDescriptor Descriptor { get; }

    /// <summary>Gets the initialized canonical extent.</summary>
    SchemaExtent? Extent { get; }

    /// <summary>Gets a diagnostic placement name; never used for implementation dispatch.</summary>
    string DiagnosticPlacement { get; }
}
