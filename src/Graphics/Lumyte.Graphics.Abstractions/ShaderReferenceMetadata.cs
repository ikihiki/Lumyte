namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides legacy immutable resource reference fixups for explicit CPU snapshot compatibility.</summary>
/// <remarks>Contains no numeric shader data or retained shader value provider. Metadata identity represents a reference revision, not a numeric data revision.</remarks>
public sealed class ShaderReferenceMetadata : IShaderReferenceMetadata
{
    /// <summary>Initializes a new instance of the <see cref="ShaderReferenceMetadata"/> class with owned reference metadata.</summary>
    /// <param name="values">The resource reference identities and their C# wire slots.</param>
    public ShaderReferenceMetadata(IEnumerable<ShaderValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = Array.AsReadOnly(values.ToArray());
    }

    /// <summary>Gets immutable resource identities derived from the legacy fixups.</summary>
    /// <exception cref="ArgumentException">A legacy fixup is not an argument-table reference.</exception>
    public IReadOnlyList<IShaderReference> References => Array.AsReadOnly(Values.Select(value => value.Reference as IShaderReference
        ?? throw new ArgumentException("A shader reference must originate from an argument table.")).ToArray());

    /// <summary>Gets the immutable resource reference fixups.</summary>
    public IReadOnlyList<ShaderValue> Values { get; }
}
