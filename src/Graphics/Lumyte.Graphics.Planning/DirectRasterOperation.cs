using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class DirectRasterOperation(IGraphicsPipeline pipeline, uint vertices, uint instances, uint firstVertex, uint firstInstance) : IRasterOperation
{
    public void Prepare(PreparedResources resources)
    {
    }

    public void SelectPipeline(IRenderEncoder encoder) => encoder.SetPipeline(pipeline);

    public void Record(IRenderEncoder encoder) => encoder.Draw(vertices, instances, firstVertex, firstInstance);

    public void PrepareNative<TArguments>(IGraphicsCommandBuffer commands, RenderPassDesc pass, GraphicsRenderStateDesc state, IArgumentTable? table, in TArguments arguments)
        where TArguments : struct, IShaderArguments
        => commands.PrepareRender(pass, pipeline, state, table, arguments);
}
