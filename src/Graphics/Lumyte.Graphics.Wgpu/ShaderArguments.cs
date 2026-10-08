using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class ShaderArguments : GpuResource
{
    private readonly DescriptorRegistration _registration;

    internal ShaderArguments(ComputePipeline pipeline, DescriptorRegistration registration, WGPUBindGroupImpl* handle)
        : base(pipeline.Owner)
    {
        Pipeline = pipeline;
        _registration = registration;
        Handle = handle;
        pipeline.Acquire();
        registration.Acquire();
    }

    internal WGPUBindGroupImpl* Handle { get; }

    internal ComputePipeline Pipeline { get; }

    protected override void ReleaseNative()
    {
        WGPU.wgpuBindGroupRelease(Handle);
        Pipeline.ReleaseLease();
        _registration.ReleaseLease();
    }
}
