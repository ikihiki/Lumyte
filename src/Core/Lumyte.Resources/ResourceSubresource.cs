namespace Lumyte.Resources;

/// <summary>Describes a discoverable document element without owning its generated output.</summary>
public sealed record ResourceSubresource
{
    /// <summary>Gets the document-defined selector.</summary>
    public required string Selector { get; init; }

    /// <summary>Gets the exact output type identities that can be requested for this element.</summary>
    public required IReadOnlyList<RuntimeTypeHandle> OutputTypes { get; init; }
}
