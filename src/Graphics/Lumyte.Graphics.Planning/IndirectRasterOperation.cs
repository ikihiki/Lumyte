using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class IndirectRasterOperation(IGraphicsPipeline pipeline, PlanBuffer<DrawIndirectArguments> buffer, PlanRange range) : IRasterOperation
{
    private BufferSlice<DrawIndirectArguments> _slice;

    public void Prepare(PreparedResources resources) => _slice = resources.Buffer(buffer, range);

    public void SelectPipeline(IRenderEncoder encoder) => encoder.SetPipeline(pipeline);

    public void Record(IRenderEncoder encoder) => encoder.DrawIndirect(_slice);

    public void PrepareNative<TArguments>(IGraphicsCommandBuffer commands, RenderPassDesc pass, GraphicsRenderStateDesc state, IArgumentTable? table, in TArguments arguments)
        where TArguments : struct, IShaderArguments
        => commands.PrepareRender(pass, pipeline, state, table, arguments);
}
