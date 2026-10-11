using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using RenderGraphBuilder = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal sealed class DispatchBatch(
    RenderPreparationContext2D preparation,
    string name,
    List<string> recorded,
    bool multiplePasses,
    Action? onAppend) : IPreparedCommandBatch2D
{
    internal int DisposeCount { get; private set; }

    public void AppendPasses(RenderGraphBuilder graph, RenderTargetBinding2D target)
    {
        if (!ReferenceEquals(graph, preparation.Graph) || !ReferenceEquals(target.Texture, preparation.Target.Texture))
        {
            throw new InvalidOperationException("The prepared batch must be appended to its original graph and target.");
        }

        onAppend?.Invoke();
        RenderGraphPass? preparationPass = null;
        if (multiplePasses)
        {
            preparationPass = graph.AddPass(name + ".prepare", context =>
            {
                _ = context.GetTexture(target.Texture);
                recorded.Add(name + ".prepare");
            }).UseTexture(target.Texture, TextureState.Sampled, new BarrierScope(PipelineStage.ComputeShader, ResourceAccess.ShaderRead));
        }

        RenderGraphPass draw = graph.AddPass(name + ".draw", context =>
        {
            _ = context.GetTexture(target.Texture);
            recorded.Add(name + ".draw");
        }).UseTexture(target.Texture, TextureState.ColorAttachment, new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
        if (preparationPass is not null)
        {
            draw.DependsOn(preparationPass);
        }
    }

    public void Dispose() => DisposeCount++;
}
