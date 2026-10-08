using Ahjo.Wgpu.Native;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class DrawingArguments : GpuResource
{
    private readonly A.Buffer _lookup;

    internal DrawingArguments(GraphicsPipeline pipeline, ShaderDataRegion region, DescriptorRegistration[] dependencies, A.Buffer lookup, WGPUBindGroupImpl* handle)
        : base(pipeline.Owner)
    {
        (Pipeline, Region, Dependencies, _lookup) = (pipeline, region, dependencies, lookup);
        Handle = handle;
        pipeline.Acquire();
        region.Buffer.Acquire();
        region.Snapshot.Acquire();
    }

    internal GraphicsPipeline Pipeline { get; }

    internal ShaderDataRegion Region { get; }

    internal DescriptorRegistration[] Dependencies { get; }

    internal WGPUBindGroupImpl* Handle { get; }

    protected override void ReleaseNative()
    {
        WGPU.wgpuBindGroupRelease(Handle);
        _lookup.Dispose();
        Pipeline.ReleaseLease();
        Region.Buffer.ReleaseLease();
        Region.Snapshot.ReleaseLease();
    }
}
