namespace Lumyte.Graphics.Native;

/// <summary>A caller-owned native attachment view. It does not extend its texture's lifetime.</summary>
public abstract class NativeGpuRenderViewHandle
{
    protected NativeGpuRenderViewHandle(NativeGpuRenderViewFlags flags) => Flags = flags;

    public NativeGpuRenderViewFlags Flags { get; }
}
