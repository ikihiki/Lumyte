using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserComputeEncoder(BrowserCommandBuffer owner, JSObject handle) : IComputeEncoder
{
    private BrowserComputePipeline? _pipeline;

    public void SetPipeline(IGraphicsComputePipeline pipeline)
    {
        owner.ValidatePass(this);
        ArgumentNullException.ThrowIfNull(pipeline);
        if (pipeline is not BrowserComputePipeline program || !ReferenceEquals(program.Owner, owner.Owner))
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

        BrowserInterop.SetComputePipeline(handle, _pipeline.Native);
        BrowserInterop.Dispatch(handle, groupCountX, groupCountY, groupCountZ);
        owner.TrackProgram(_pipeline.ValidateAlive);
    }

    public void End() => owner.EndCompute(this, handle);
}
