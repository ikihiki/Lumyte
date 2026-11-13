namespace Lumyte.Graphics.Abstractions;

/// <summary>Holds only immutable, non-owning resource reference fixup metadata.</summary>
/// <remarks>Contains no numeric shader data or retained shader value provider. Metadata identity represents a reference revision, not a numeric data revision.</remarks>
public sealed class ShaderReferenceMetadata
{
    /// <summary>Initializes a new instance of the <see cref="ShaderReferenceMetadata"/> class with owned reference metadata.</summary>
    /// <param name="values">The resource reference identities and their C# wire slots.</param>
    public ShaderReferenceMetadata(IEnumerable<ShaderValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = Array.AsReadOnly(values.ToArray());
    }

    /// <summary>Gets the immutable resource reference fixups.</summary>
    public IReadOnlyList<ShaderValue> Values { get; }
}
