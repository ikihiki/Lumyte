using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal sealed class ProgramTestPipeline(GraphicsPipelineDesc desc, Action release) : IGraphicsPipeline
{
    public GraphicsPipelineDesc Desc { get; } = desc;

    public void Dispose() => release();
}
