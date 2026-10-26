using System.Threading.Tasks.Sources;

namespace Lumyte.Execution;

/// <summary>Implements admission for asynchronous CPU workflows.</summary>
public sealed partial class ExecutionRuntime
{
    /// <summary>Checks whether the current workflow dispatch has consumed its synchronous time budget.</summary>
    /// <param name="budget">The positive elapsed-time budget for one dispatch.</param>
    /// <returns>True when the current workflow should yield; false outside an active workflow dispatch.</returns>
    /// <remarks>
    /// The budget restarts on every admitted continuation, so time spent suspended or queued does not count.
    /// This is an elapsed-time checkpoint, not preemption or a measurement of processor usage.
    /// </remarks>
    public static bool ShouldYield(TimeSpan budget)
    {
        if (budget <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(budget));
        }

        return _executing is not null && _workflowExecuting
            && SynchronizationContext.Current is IWorkflowContext context
            && ReferenceEquals(context.Runtime, _executing) && context.ShouldYield(budget);
    }

    internal CommandEnqueueStatus EnqueueWorkflow<TCommand, TResult>(WorkflowDefinition<TCommand, TResult> work, in TCommand command, CancellationToken token, out IValueTaskSource<TResult>? completion)
        where TCommand : struct
    {
        VerifyWorkflowTarget();
        completion = null;
        lock (Sync)
        {
            bool workflowChild = _executing == this && _workflowExecuting;
            if (_status != ExecutionStatus.Running && !(_status == ExecutionStatus.Quiescing && workflowChild))
            {
                return CommandEnqueueStatus.NotRunning;
            }

            if (token.IsCancellationRequested)
            {
                return CommandEnqueueStatus.Canceled;
            }

            if (!Reserve(1, 1, work.PayloadBytes, false, 0))
            {
                return CommandEnqueueStatus.QueueFull;
            }

            PendingWorkflow<TCommand, TResult> pending;
            try
            {
                pending = new PendingWorkflow<TCommand, TResult>(work, in command, token) { ContinueOnShutdown = workflowChild };
                EnqueueAccepted(pending);
            }
            catch
            {
                Release(1, 1, work.PayloadBytes, false, 0);
                throw;
            }

            completion = pending;
            Notify(false);
            return CommandEnqueueStatus.Accepted;
        }
    }
}
