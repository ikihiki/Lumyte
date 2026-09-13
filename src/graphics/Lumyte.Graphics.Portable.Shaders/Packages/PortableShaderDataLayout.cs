namespace Lumyte.Graphics.Portable.Shaders;

/// <summary>Prepared host ABI metadata; names and strides are not parsed or inferred at runtime.</summary>
public readonly record struct PortableShaderFieldLayout(
    string Name, string TypeName, uint Offset, uint Size, uint Alignment,
    uint ArrayStride = 0, uint MatrixStride = 0);

/// <summary>An immutable root or parameter type layout produced for the final WGSL module.</summary>
/// <remarks>Loading does not inspect this metadata to create, populate, or upload parameter buffers.</remarks>
public sealed class PortableShaderDataLayout
{
    public PortableShaderDataLayout(string name, uint size, uint alignment,
        ReadOnlySpan<PortableShaderFieldLayout> fields = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        Size = size;
        Alignment = alignment;
        Fields = Array.AsReadOnly(fields.ToArray());
    }

    public string Name { get; }
    public uint Size { get; }
    public uint Alignment { get; }
    public IReadOnlyList<PortableShaderFieldLayout> Fields { get; }
}
