using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationComputePipeline : ValidationResource<IGraphicsComputePipeline>, IGraphicsComputePipeline
{
    private readonly ComputePipelineDesc _desc;

    internal ValidationComputePipeline(ValidationDevice device, IGraphicsComputePipeline raw, ComputePipelineDesc desc)
        : base(device, raw, device)
    {
        _desc = desc;
    }

    public ComputePipelineDesc Desc
    {
        get
        {
            ValidateAlive();
            return _desc;
        }
    }
}
