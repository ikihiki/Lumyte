using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class IndexedRasterOperation(IGraphicsPipeline pipeline, IIndexBinding binding, uint count, uint instances, uint firstIndex, int baseVertex, uint firstInstance) : IRasterOperation
{
    public void Prepare(PreparedResources resources) => binding.Prepare(resources);

    public void SelectPipeline(IRenderEncoder encoder) => encoder.SetPipeline(pipeline);

    public void Record(IRenderEncoder encoder) => encoder.DrawIndexed(count, instances, firstIndex, baseVertex, firstInstance);

    public void PrepareNative<TArguments>(IGraphicsCommandBuffer commands, RenderPassDesc pass, GraphicsRenderStateDesc state, IArgumentTable? table, in TArguments arguments)
        where TArguments : struct, IShaderArguments
        => commands.PrepareRender(pass, pipeline, state, table, arguments, binding.Format);

    public void BindInputs(IRenderEncoder encoder) => binding.Record(encoder);
}
