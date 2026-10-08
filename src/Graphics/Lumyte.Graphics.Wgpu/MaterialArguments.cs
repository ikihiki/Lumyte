using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class MaterialArguments : GpuResource
{
    internal MaterialArguments(GraphicsPipeline pipeline, MaterialRegion region, WGPUBindGroupImpl* handle)
        : base(pipeline.Owner)
    {
        Pipeline = pipeline;
        Region = region;
        Handle = handle;
        pipeline.Acquire();
        region.Buffer.Acquire();
        region.Bindings.Acquire();
    }

    internal GraphicsPipeline Pipeline { get; }

    internal MaterialRegion Region { get; }

    internal WGPUBindGroupImpl* Handle { get; }

    protected override void ReleaseNative()
    {
        WGPU.wgpuBindGroupRelease(Handle);
        Pipeline.ReleaseLease();
        Region.Buffer.ReleaseLease();
        Region.Bindings.ReleaseLease();
    }
}
