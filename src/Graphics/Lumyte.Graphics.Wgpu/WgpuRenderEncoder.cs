using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuRenderEncoder(WgpuCommandBuffer owner, WGPURenderPassEncoderImpl* handle) : IRenderEncoder
{
    public void End() => owner.EndRender(this, handle);
}
