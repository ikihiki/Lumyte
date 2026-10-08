using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class ShaderArguments : GpuResource
{
    private readonly WgpuBuffer _buffer;

    internal ShaderArguments(ComputePipeline pipeline, WgpuBuffer buffer, WGPUBindGroupImpl* handle)
        : base(pipeline.Owner)
    {
        Pipeline = pipeline;
        _buffer = buffer;
        Handle = handle;
        pipeline.Acquire();
        buffer.Acquire();
    }

    internal WGPUBindGroupImpl* Handle { get; }

    internal ComputePipeline Pipeline { get; }

    protected override void ReleaseNative()
    {
        WGPU.wgpuBindGroupRelease(Handle);
        Pipeline.ReleaseLease();
        _buffer.ReleaseLease();
    }
}
