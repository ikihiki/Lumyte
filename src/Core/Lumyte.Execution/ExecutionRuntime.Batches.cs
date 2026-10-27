namespace Lumyte.Execution;

/// <summary>Implements bounded execution scheduling and admission.</summary>
public sealed partial class ExecutionRuntime
{
    /// <summary>Atomically admits a batch, reporting stopped, canceled and full admission as statuses.</summary>
    /// <param name="batch">Single-producer staging, retained unchanged on rejection.</param>
    /// <param name="run">The accepted run, or null on rejection.</param>
    /// <param name="cancellationToken">Cancellation before handler start.</param>
    /// <returns>The admission status.</returns>
    public CommandEnqueueStatus Submit(CommandBatch batch, out CommandRun? run, CancellationToken cancellationToken = default)
        => SubmitCore(batch, out run, cancellationToken);

    private CommandEnqueueStatus SubmitCore(CommandBatch batch, out CommandRun? run, CancellationToken token)
    {
        VerifyOutsideHandler();
        run = null;
        ArgumentNullException.ThrowIfNull(batch);

        lock (Sync)
        {
            CommandEnqueueStatus admission = GetNormalAdmissionStatus(token);
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

    private CommandEnqueueStatus GetNormalAdmissionStatus(CancellationToken token)
    {
        if (_status != ExecutionStatus.Running)
        {
            return CommandEnqueueStatus.NotRunning;
        }

        return token.IsCancellationRequested ? CommandEnqueueStatus.Canceled : CommandEnqueueStatus.Accepted;
    }
}
