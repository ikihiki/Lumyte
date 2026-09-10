namespace Lumyte.Graphics.Native;

/// <summary>A caller-owned compute pipeline. The caller resolves recorded references and GPU use before destruction.</summary>
public abstract class NativeGpuComputePipelineHandle
{
    protected NativeGpuComputePipelineHandle() { }
}
