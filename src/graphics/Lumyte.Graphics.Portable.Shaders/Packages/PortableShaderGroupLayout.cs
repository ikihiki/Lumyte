namespace Lumyte.Graphics.Portable.Shaders;

/// <summary>An immutable group declaration. Its position in the package is its group number.</summary>
public sealed class PortableShaderGroupLayout
{
    public PortableShaderGroupLayout(ReadOnlySpan<GpuBindingLayoutEntry> entries)
    {
        Entries = Array.AsReadOnly(entries.ToArray());
    }

    public IReadOnlyList<GpuBindingLayoutEntry> Entries { get; }
}
