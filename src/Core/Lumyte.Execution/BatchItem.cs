using System.Runtime.ExceptionServices;

namespace Lumyte.Execution;

internal abstract class BatchItem(WorkDefinition definition) : QueuedWork(definition, default)
{
    internal CommandRun? Run { get; set; }

    internal ExceptionDispatchInfo? Error { get; set; }

    internal bool Succeeded { get; set; }

    internal override bool IsCanceled => Run!.IsCancellationRequested;

    internal override void Finish(Exception? error)
    {
        if (error is null)
        {
            Succeeded = true;
        }
        else
        {
            Error = ExceptionDispatchInfo.Capture(error);
        }

        Run!.ItemFinished(error, CanceledBeforeStart);
    }

    internal abstract void Clear();
}
