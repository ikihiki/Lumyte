using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal sealed class SmokePipeline(GraphicsPipelineDesc desc) : IGraphicsPipeline
{
    public GraphicsPipelineDesc Desc { get; } = desc;

    public void Dispose()
    {
    }
}
