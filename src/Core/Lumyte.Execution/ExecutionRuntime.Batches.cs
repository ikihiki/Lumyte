namespace Lumyte.Execution;

/// <summary>Implements bounded execution scheduling and admission.</summary>
public sealed partial class ExecutionRuntime
{
    /// <summary>Atomically admits all staged commands, preserving shared group FIFO.</summary>
    /// <param name="batch">Single-producer staging.</param>
    /// <param name="run">The accepted run, or null on capacity shortage.</param>
    /// <param name="token">Cancellation before handler start.</param>
    /// <returns>False only when retained capacity is exhausted.</returns>
    public bool TrySubmit(CommandBatch batch, out CommandRun? run, CancellationToken token = default)
    {
        VerifyOutsideHandler();
        token.ThrowIfCancellationRequested();
        lock (Sync)
        {
            RequireRunning();
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
                return false;
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
            return true;
        }
    }

    private void RequireRunning()
    {
        if (_status != ExecutionStatus.Running)
        {
            throw new InvalidOperationException("Normal admission requires a running runtime.");
        }
    }
}
