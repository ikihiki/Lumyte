namespace Lumyte.Graphics.Native;

/// <summary>Reserved bytes and placement alignment, which may exceed the logical data size.</summary>
public readonly struct NativeGpuMemoryRequirements
{
    public NativeGpuMemoryRequirements(ulong size, ulong alignment, NativeGpuMemoryCompatibility compatibility)
    {
        ArgumentNullException.ThrowIfNull(compatibility);
        Size = size;
        Alignment = alignment;
        Compatibility = compatibility;
    }

    public ulong Size { get; }
    public ulong Alignment { get; }
    public NativeGpuMemoryCompatibility Compatibility { get; }
}
