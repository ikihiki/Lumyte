using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationPipeline : ValidationResource<IGraphicsPipeline>, IGraphicsPipeline
{
    private readonly GraphicsPipelineDesc _desc;

    internal ValidationPipeline(ValidationDevice device, IGraphicsPipeline raw, GraphicsPipelineDesc desc)
        : base(device, raw, device)
    {
        _desc = desc;
    }

    public GraphicsPipelineDesc Desc
    {
        get
        {
            ValidateAlive();
            return _desc;
        }
    }
}
