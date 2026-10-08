using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class Sampler : GpuResource
{
    internal Sampler(WgpuDevice owner, WGPUSamplerImpl* handle)
        : base(owner)
    {
        Handle = handle;
    }

    internal WGPUSamplerImpl* Handle { get; }

    protected override void ReleaseNative() => WGPU.wgpuSamplerRelease(Handle);
}
