using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Tests;

internal sealed class DispatchRenderers :
    IFillRectRenderer2D,
    IFillRoundedRectRenderer2D,
    IFillCircleRenderer2D,
    IFillPathRenderer2D
{
    internal List<DrawingCommand2D> Commands { get; } = [];

    internal List<DispatchBatch> Batches { get; } = [];

    internal List<string> Recorded { get; } = [];

    internal Action<string>? OnPreparing { get; init; }

    internal Action? OnAppend { get; init; }

    internal bool MultiplePasses { get; init; }

    ValueTask<IPreparedCommandBatch2D> IFillRectRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken) => Prepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IFillRoundedRectRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken) => Prepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IFillCircleRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken) => Prepare(context, commands);

    ValueTask<IPreparedCommandBatch2D> IFillPathRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken) => Prepare(context, commands);

    internal DrawingCommandRenderers2D CreateTable() =>
        new(this, this, this, this);

    private ValueTask<IPreparedCommandBatch2D> Prepare<T>(RenderPreparationContext2D context, ReadOnlyMemory<T> commands)
        where T : DrawingCommand2D
    {
        OnPreparing?.Invoke(typeof(T).Name);
        if (context.Target.LoadOp != AttachmentLoadOp.Load)
        {
            throw new InvalidOperationException("The host must normalize the renderer target to Load.");
        }

        foreach (T command in commands.Span)
        {
            Commands.Add(command);
        }

        var batch = new DispatchBatch(context, typeof(T).Name, Recorded, MultiplePasses, OnAppend);
        Batches.Add(batch);
        return ValueTask.FromResult<IPreparedCommandBatch2D>(batch);
    }
}
