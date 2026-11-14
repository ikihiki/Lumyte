namespace Lumyte.Graphics.Abstractions;

/// <summary>Exposes ordered, non-owning resource dependencies without shader bytes or wire fixups.</summary>
/// <remarks>Metadata identity represents a reference revision, not a numeric data revision.</remarks>
public interface IShaderReferenceMetadata
{
    /// <summary>Gets the immutable, ordered resource reference identities.</summary>
    IReadOnlyList<IShaderReference> References { get; }
}
