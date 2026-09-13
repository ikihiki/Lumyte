namespace Lumyte.Graphics.Portable;

/// <summary>Immutable shader entries, group-ordered layouts and direct input size, independent of shader packages.</summary>
/// <remarks>Input spans are copied. The caller keeps the referenced modules and layouts alive through their pipeline uses.</remarks>
public sealed class GpuShaderProgramDescription
{
    public GpuShaderProgramDescription(ReadOnlySpan<GpuShaderEntryPoint> entryPoints,
        ReadOnlySpan<GpuBindingLayoutHandle> bindingLayouts, uint immediateSize = 0)
    {
        EntryPoints = Array.AsReadOnly(entryPoints.ToArray());
        BindingLayouts = Array.AsReadOnly(bindingLayouts.ToArray());
        ImmediateSize = immediateSize;
    }

    public IReadOnlyList<GpuShaderEntryPoint> EntryPoints { get; }
    public IReadOnlyList<GpuBindingLayoutHandle> BindingLayouts { get; }
    public uint ImmediateSize { get; }
}
