namespace Lumyte.Execution;

internal abstract class QueuedWork(WorkDefinition? definition, CancellationToken token)
{
    internal WorkDefinition? Definition { get; set; } = definition;

    internal QueuedWork? Next { get; set; }

    internal bool Lifecycle { get; set; }

    internal bool CanceledBeforeStart { get; set; }

    internal CancellationToken Token { get; set; } = token;

    internal long Sequence { get; set; }

    internal virtual bool KeepAlive => false;

    internal virtual bool IsCanceled => Token.IsCancellationRequested;

    internal abstract void Perform(in WorkContext context);

    internal abstract void Finish(Exception? error);
}
