using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuComputeEncoder(WgpuCommandBuffer owner, WGPUComputePassEncoderImpl* handle) : IComputeEncoder
{
    private WgpuComputePipeline? _pipeline;

    public void SetPipeline(IGraphicsComputePipeline pipeline)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not WgpuComputePipeline program || !ReferenceEquals(program.Owner, owner.Owner))
        {
            throw new ArgumentException("Compute program belongs to another device.");
        }

        program.ValidateAlive();
        _pipeline = program;
    }

    public void Dispatch(uint groupCountX, uint groupCountY = 1, uint groupCountZ = 1)
    {
        owner.ValidatePass(this);
        if (_pipeline == null)
        {
            throw new InvalidOperationException("Select a compute program before dispatch.");
        }

        _pipeline.ValidateAlive();
        uint limit = owner.Owner.Caps.MaxComputeWorkgroupsPerDimension;
        if (groupCountX == 0 || groupCountY == 0 || groupCountZ == 0 || groupCountX > limit || groupCountY > limit || groupCountZ > limit)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCountX));
        }

        WGPU.wgpuComputePassEncoderSetPipeline(handle, _pipeline.Native);
        WGPU.wgpuComputePassEncoderDispatchWorkgroups(handle, groupCountX, groupCountY, groupCountZ);
        owner.TrackProgram(_pipeline.ValidateAlive);
    }

    public void End() => owner.EndCompute(this, handle);
}
