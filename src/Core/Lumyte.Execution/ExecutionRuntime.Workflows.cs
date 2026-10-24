using System.Threading.Tasks.Sources;

namespace Lumyte.Execution;

/// <summary>Implements admission for asynchronous CPU workflows.</summary>
public sealed partial class ExecutionRuntime
{
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
