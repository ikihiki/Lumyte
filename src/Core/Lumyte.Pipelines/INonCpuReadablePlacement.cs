namespace Lumyte.Pipelines;

/// <summary>Describes an immutable opaque declaration that cannot be read by CPU callbacks.</summary>
/// <remarks>Use a default-describable immutable value type so schemas can be captured before input admission. Extent may be null only for an uninitialized value, which cannot be admitted.</remarks>
public interface INonCpuReadablePlacement
{
    /// <summary>Gets the canonical descriptor.</summary>
    SchemaDescriptor Descriptor { get; }

    /// <summary>Gets the initialized canonical extent.</summary>
    SchemaExtent? Extent { get; }

    /// <summary>Gets a diagnostic placement name; never used for implementation dispatch.</summary>
    string DiagnosticPlacement { get; }
}
