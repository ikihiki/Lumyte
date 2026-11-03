using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class RetainedBatchRun2D(IReadOnlyList<IPreparedCommandBatch2D> batches, bool selectedBuiltins) : IRetainedCommandBatch2D
{
    private int _remaining = batches.Count;

    public long EstimatedBytes => batches.OfType<IRetainedCommandBatch2D>().Sum(batch => batch.EstimatedBytes);

    internal bool Reusable => selectedBuiltins && batches.All(batch => batch is IRetainedCommandBatch2D);

    public void DeclareResources(IDrawingResourceDeclarations2D resources)
    {
        foreach (IPreparedCommandBatch2D batch in batches)
        {
            batch.DeclareResources(resources);
        }
    }

    public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target) => RecordRetained(commands, target, false);

    public void RecordRetained(IGraphicsCommandBuffer commands, IGraphicsTextureView target, bool resident)
    {
        for (int i = 0; i < batches.Count; i++)
        {
            if (i > 0)
            {
                var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
                commands.Barrier(new TextureBarrierDesc { Texture = target.Texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
            }

            if (selectedBuiltins && batches[i] is IRetainedCommandBatch2D retained)
            {
                retained.RecordRetained(commands, target, resident);
            }
            else
            {
                batches[i].Record(commands, target);
            }
        }
    }

    public void Dispose()
    {
        while (_remaining != 0)
        {
            batches[_remaining - 1].Dispose();
            _remaining--;
        }
    }
}
