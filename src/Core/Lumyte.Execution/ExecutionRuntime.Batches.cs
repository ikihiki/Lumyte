using System.Diagnostics.CodeAnalysis;

namespace Lumyte.Execution;

/// <summary>Implements bounded execution scheduling and admission.</summary>
public sealed partial class ExecutionRuntime
{
    /// <summary>Atomically admits all staged commands, preserving shared group FIFO.</summary>
    /// <param name="batch">Single-producer staging.</param>
    /// <param name="run">The accepted run, or null on capacity shortage.</param>
    /// <param name="token">Cancellation before handler start.</param>
    /// <returns>False only when retained capacity is exhausted.</returns>
    public bool TrySubmit(CommandBatch batch, [NotNullWhen(true)] out CommandRun? run, CancellationToken token = default)
        => SubmitCore(batch, out run, token, detailed: false) == CommandEnqueueStatus.Accepted && run is not null;

    /// <summary>Atomically admits a batch, reporting stopped, canceled and full admission as statuses.</summary>
    /// <param name="batch">Single-producer staging, retained unchanged on rejection.</param>
    /// <param name="run">The accepted run, or null on rejection.</param>
    /// <param name="cancellationToken">Cancellation before handler start.</param>
    /// <returns>The admission status.</returns>
    public CommandEnqueueStatus Submit(CommandBatch batch, out CommandRun? run, CancellationToken cancellationToken = default)
        => SubmitCore(batch, out run, cancellationToken, detailed: true);

    private CommandEnqueueStatus SubmitCore(CommandBatch batch, out CommandRun? run, CancellationToken token, bool detailed)
    {
        VerifyOutsideHandler();
        run = null;
        ArgumentNullException.ThrowIfNull(batch);
        if (!detailed)
        {
            token.ThrowIfCancellationRequested();
        }

        lock (Sync)
        {
            CommandEnqueueStatus admission = GetNormalAdmissionStatus(token, detailed);
            if (admission != CommandEnqueueStatus.Accepted)
            {
                return admission;
            }

            batch.CheckMutable();
            if (batch.Items.Count == 0)
            {
                throw new ArgumentException("An empty batch cannot be submitted.", nameof(batch));
            }

            GroupDefinition? group = batch.Items[0].Definition!.Group;
            long bytes = 0;
            foreach (BatchItem item in batch.Items)
            {
                WorkDefinition work = item.Definition!;
                if (work.Module.Runtime != this || work.Admission == WorkAdmission.Startup || work.Group != group)
                {
                    throw new ArgumentException("Batch must contain local work in one owner group, or only unordered CPU work.", nameof(batch));
                }

                bytes = checked(bytes + work.PayloadBytes);
            }

            if (!Reserve(1, batch.Items.Count, bytes, false, 0))
            {
                run = null;
                return CommandEnqueueStatus.QueueFull;
            }

            BatchItem[] items = batch.Items.ToArray();
            run = new CommandRun(this, batch, items, bytes, token);
            batch.ActiveRun = run;
            foreach (BatchItem item in items)
            {
                item.Run = run;
                item.Token = token;
                EnqueueAccepted(item);
            }

            Notify(false);
            return CommandEnqueueStatus.Accepted;
        }
    }

    private CommandEnqueueStatus GetNormalAdmissionStatus(CancellationToken token, bool detailed)
    {
        if (!detailed)
        {
            RequireRunning();
            return CommandEnqueueStatus.Accepted;
        }

        if (_status != ExecutionStatus.Running)
        {
            return CommandEnqueueStatus.NotRunning;
        }

        return token.IsCancellationRequested ? CommandEnqueueStatus.Canceled : CommandEnqueueStatus.Accepted;
    }

    private void RequireRunning()
    {
        if (_status != ExecutionStatus.Running)
        {
            throw new InvalidOperationException("Normal admission requires a running runtime.");
        }
    }
}
