namespace Lumyte.Graphics.Portable.Shaders;

/// <summary>A semantic input name mapped to one declared group binding and resource kind.</summary>
public readonly record struct PortableShaderBindingSchemaEntry(
    string Name, uint Group, uint Binding, GpuBindingLayoutKind Kind);

/// <summary>Immutable metadata for generating low-level inputs and optional upper-layer integration.</summary>
public sealed class PortableShaderBindingSchema
{
    public PortableShaderBindingSchema(ReadOnlySpan<PortableShaderBindingSchemaEntry> entries)
    {
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    public IReadOnlyList<PortableShaderBindingSchemaEntry> Entries { get; }
}
