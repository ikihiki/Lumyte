namespace Lumyte.Resources;

/// <summary>Describes a discoverable document element without owning its generated output.</summary>
public sealed record ResourceSubresource
{
    /// <summary>Gets the document-defined selector.</summary>
    public required string Selector { get; init; }

    /// <summary>Gets the element kind, such as mesh, image or animation.</summary>
    public required string Kind { get; init; }

    /// <summary>Gets the registered pipelines that can produce this element.</summary>
    public required IReadOnlyList<string> PipelineIds { get; init; }
}
