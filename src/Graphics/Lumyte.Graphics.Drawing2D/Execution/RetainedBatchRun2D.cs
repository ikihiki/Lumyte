using System.Runtime.ExceptionServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class RetainedBatchRun2D(IReadOnlyList<IPreparedCommandBatch2D> batches, bool selectedBuiltins) : IRetainedCommandBatch2D, IRetainedMemory2D
{
    private int _remaining = batches.Count;
    private List<IPreparedCommandBatch2D>? _failedCleanup;

    public long EstimatedBytes => batches.OfType<IRetainedCommandBatch2D>().Sum(batch => batch.EstimatedBytes);

    internal bool Reusable => selectedBuiltins && batches.All(batch => batch is IRetainedCommandBatch2D);

    public void AccountMemory(Dictionary<object, long> owners)
    {
        foreach (IPreparedCommandBatch2D batch in batches)
        {
            if (batch is IRetainedMemory2D shared)
            {
                shared.AccountMemory(owners);
            }
            else if (batch is IRetainedCommandBatch2D retained)
            {
                owners.TryAdd(batch, retained.EstimatedBytes);
            }
        }
    }

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
        List<IPreparedCommandBatch2D>? previousFailures = _failedCleanup;
        List<Exception>? failures = null;
        _failedCleanup = null;
        if (previousFailures is not null)
        {
            foreach (IPreparedCommandBatch2D batch in previousFailures)
            {
                TryDispose(batch);
            }
        }

        while (_remaining != 0)
        {
            TryDispose(batches[--_remaining]);
        }

        if (failures is { Count: 1 })
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException("Batch cleanup failed; incomplete batches remain owned for retry.", failures);
        }

        void TryDispose(IPreparedCommandBatch2D batch)
        {
            try
            {
                batch.Dispose();
            }
            catch (Exception failure)
            {
                (_failedCleanup ??= []).Add(batch);
                (failures ??= []).Add(failure);
            }
        }
    }
}
