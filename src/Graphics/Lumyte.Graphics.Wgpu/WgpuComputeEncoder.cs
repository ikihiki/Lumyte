using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuComputeEncoder(WgpuCommandBuffer owner, WGPUComputePassEncoderImpl* handle) : IComputeEncoder
{
    public void End() => owner.EndCompute(this, handle);
}
