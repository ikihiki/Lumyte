using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class MeshRasterOperation(IMeshPipeline pipeline, uint x, uint y, uint z) : IRasterOperation
{
    public void Prepare(PreparedResources resources)
    {
    }

    public void SelectPipeline(IRenderEncoder encoder) => encoder.SetPipeline(pipeline);

    public void Record(IRenderEncoder encoder) => encoder.DrawMeshTasks(x, y, z);

    public void PrepareNative<TArguments>(IGraphicsCommandBuffer commands, RenderPassDesc pass, GraphicsRenderStateDesc state, IArgumentTable? table, in TArguments arguments)
        where TArguments : struct, IShaderArguments
        => commands.PrepareMesh(pass, pipeline, state, table, arguments);
}
