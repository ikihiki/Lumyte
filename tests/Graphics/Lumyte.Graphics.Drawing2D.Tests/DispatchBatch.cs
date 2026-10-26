using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal sealed class DispatchBatch(
    RenderPreparationContext2D preparation,
    string name,
    List<string> recorded,
    bool multiplePasses,
    Action? onAppend) : IPreparedCommandBatch2D
{
    internal int DisposeCount { get; private set; }

    public void DeclareResources(IDrawingResourceDeclarations2D resources) => onAppend?.Invoke();

    public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
    {
        if (target.Texture.Width != preparation.Target.Description.Width)
        {
            throw new InvalidOperationException("The batch target must match its prepared geometry.");
        }

        if (multiplePasses)
        {
            recorded.Add(name + ".prepare");
            commands.Barrier(new MemoryBarrierDesc { Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.VertexShader, ResourceAccess.ShaderRead) });
        }

        recorded.Add(name + ".draw");
    }

    public void Dispose() => DisposeCount++;
}
