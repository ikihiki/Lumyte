using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class IndexedIndirectRasterOperation(IGraphicsPipeline pipeline, IIndexBinding binding, PlanBuffer<DrawIndexedIndirectArguments> buffer, PlanRange range) : IRasterOperation
{
    private BufferSlice<DrawIndexedIndirectArguments> _slice;

    public void Prepare(PreparedResources resources)
    {
        binding.Prepare(resources);
        _slice = resources.Buffer(buffer, range);
    }

    public void SelectPipeline(IRenderEncoder encoder) => encoder.SetPipeline(pipeline);

    public void Record(IRenderEncoder encoder) => encoder.DrawIndexedIndirect(_slice);

    public void PrepareNative<TArguments>(IGraphicsCommandBuffer commands, RenderPassDesc pass, GraphicsRenderStateDesc state, IArgumentTable? table, in TArguments arguments)
        where TArguments : struct, IShaderArguments
        => commands.PrepareRender(pass, pipeline, state, table, arguments, binding.Format);

    public void BindInputs(IRenderEncoder encoder) => binding.Record(encoder);
}
