using System.Diagnostics;

namespace Lumyte.Execution;

/// <summary>Implements bounded execution scheduling and admission.</summary>
public sealed partial class ExecutionRuntime
{
    private int _activeCpu;

    private int DrainGroups(List<GroupDefinition> groups, ref int cursor, int maxCount, TimeSpan maxElapsed, bool lifecycleOnly, long limit, long deadline = long.MaxValue)
    {
        long started = Stopwatch.GetTimestamp();
        int count = 0;
        while (count < maxCount)
        {
            long now = Stopwatch.GetTimestamp();
            if (now >= deadline || Stopwatch.GetElapsedTime(started, now) >= maxElapsed)
            {
                break;
            }

            QueuedWork? pending = null;
            Exception? error = null;
            lock (Sync)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    GroupDefinition group = groups[cursor++ % groups.Count];
                    cursor %= groups.Count;
                    if (group.Head is { } candidate && candidate.Sequence <= limit
                        && (!lifecycleOnly || candidate.Lifecycle || _status == ExecutionStatus.Quiescing)
                        && CanClaim(candidate, out error))
                    {
                        pending = candidate;
                        group.Head = candidate.Next;
                        candidate.Next = null;
                        if (group.Head is null)
                        {
                            group.Tail = null;
                        }

                        break;
                    }
                }
            }

            if (pending is null)
            {
                break;
            }

            Execute(pending, error);
            count++;
        }

        return count;
    }

    private bool CanClaim(QueuedWork candidate, out Exception? error)
    {
        error = null;
        if (candidate.IsCanceled || (_cancelPending && candidate.CancelOnShutdown && candidate.Definition?.Admission == WorkAdmission.Normal))
        {
            candidate.CanceledBeforeStart = true;
            error = new OperationCanceledException(candidate.Token);
            return true;
        }

        if (candidate.Definition is not { } work)
        {
            return true;
        }

        if (_status != ExecutionStatus.Running && !candidate.Lifecycle && _status != ExecutionStatus.Quiescing)
        {
            return false;
        }

        foreach (GateDefinition gate in work.Gates)
        {
            if (gate.Failure is { } failure)
            {
                error = failure;
                return true;
            }
        }

        return work.Gates.All(gate => gate.Open);
    }

    private void Execute(QueuedWork pending, Exception? error, bool cpuWorker = false)
    {
        WorkDefinition? definition = pending.Definition;
        ExecutionRuntime? previousRuntime = _executing;
        bool previousWorkflow = _workflowExecuting;
        try
        {
            if (error is null)
            {
                _executing = this;
                _workflowExecuting = pending.IsWorkflow;
                var context = new WorkContext(this, definition?.Owner);
                context.VerifyAccess();
                pending.Perform(in context);
            }
        }
        catch (Exception failure)
        {
            error = failure;
        }
        finally
        {
            _executing = previousRuntime;
            _workflowExecuting = previousWorkflow;
        }

        lock (Sync)
        {
            if (cpuWorker)
            {
                _activeCpu--;
            }

            // Decide completion and publish reusable work atomically; continuations never run inline.
            WorkFinish finish = pending.Finish(error);
            if (finish == WorkFinish.Requeue)
            {
                QueueReady(pending);
            }
            else if (finish != WorkFinish.Suspended)
            {
                pending.Definition = null;
                if (finish == WorkFinish.Completed)
                {
                    FinishPending();
                }
            }

            Notify(error is not null);
        }
    }

    private void FinishPending()
    {
        if (--_pending == 0)
        {
            _quiet?.TrySetResult();
        }
    }

    private void ScheduleWorkers()
    {
        if (_cpuMode != CpuExecutionMode.ThreadPool || _status is ExecutionStatus.Unprepared or ExecutionStatus.Created or ExecutionStatus.Stopped)
        {
            return;
        }

        int wanted = _activeCpu;
        foreach (QueuedWork item in _cpu)
        {
            if (wanted == _maxCpu)
            {
                break;
            }

            if (CanClaim(item, out _))
            {
                wanted++;
            }
        }

        if (HasGraphPropagation && wanted == _activeCpu && wanted < _maxCpu)
        {
            wanted++;
        }

        while (_workers < wanted)
        {
            _workers++;
            ThreadPool.UnsafeQueueUserWorkItem(_cpuWorker, preferLocal: false);
        }
    }

    private void RunWorker()
    {
        try
        {
            DrainCpu(64, TimeSpan.FromMilliseconds(10), true);
        }
        catch (Exception error)
        {
            RecordFailure(error);
        }
        finally
        {
            lock (Sync)
            {
                _workers--;
                ScheduleWorkers();
            }
        }
    }

    private int DrainCpu(int maxCount, TimeSpan maxElapsed, bool executeHandlers)
    {
        bool cpuWorker = executeHandlers && _cpuMode == CpuExecutionMode.ThreadPool;
        long started = Stopwatch.GetTimestamp();
        int count = 0;
        while (count < maxCount && Stopwatch.GetElapsedTime(started) < maxElapsed)
        {
            QueuedWork? pending = null;
            Exception? error = null;
            lock (Sync)
            {
                if (PumpGraphPropagation())
                {
                    count++;
                    continue;
                }

                int length = _cpu.Count;
                for (int i = 0; i < length; i++)
                {
                    QueuedWork candidate = _cpu.Dequeue();
                    if (CanClaim(candidate, out error) && (executeHandlers || error is not null))
                    {
                        pending = candidate;
                        if (cpuWorker)
                        {
                            _activeCpu++;
                        }

                        break;
                    }

                    _cpu.Enqueue(candidate);
                }
            }

            if (pending is null)
            {
                break;
            }

            Execute(pending, error, cpuWorker);
            count++;
        }

        return count;
    }

    private bool RunUpdate(UpdateDefinition update, ThreadLoopOptions? options)
    {
        UpdateTick tick;
        lock (Sync)
        {
            if (_status != ExecutionStatus.Running || update.Gates.Any(gate => !gate.Open || gate.Failure is not null))
            {
                return false;
            }

            long now = Stopwatch.GetTimestamp();
            if (options is not null && now < update.Next)
            {
                return false;
            }

            long skipped = 0;
            long scheduled = now;
            if (options is not null)
            {
                long period = Math.Max(1, (long)(options.Period.TotalSeconds * Stopwatch.Frequency));
                skipped = Math.Max(0, (now - update.Next) / period);
                scheduled = update.Next + (skipped * period);
                update.Next = scheduled + period;
            }

            update.Index += skipped + 1;
            tick = new UpdateTick(Stopwatch.GetElapsedTime(update.Previous, now), update.Index, skipped, TimeSpan.FromSeconds((double)scheduled / Stopwatch.Frequency), TimeSpan.FromSeconds((double)now / Stopwatch.Frequency));
            update.Previous = now;
            if (_pending++ == 0)
            {
                _quiet = null;
            }
        }

        try
        {
            _executing = this;
            var context = new WorkContext(this, update.Owner);
            context.VerifyAccess();
            update.Invoke(in tick, in context);
        }
        catch (Exception error)
        {
            RecordFailure(error);
        }
        finally
        {
            _executing = null;
            lock (Sync)
            {
                FinishPending();
            }
        }

        return true;
    }

    private void RecordFailure(Exception error)
    {
        lock (Sync)
        {
            _failure ??= error;
            if (_status == ExecutionStatus.Running)
            {
                _status = ExecutionStatus.Quiescing;
                _cancelPending = true;
            }

            Notify();
        }
    }

    private void ThreadLoop(ThreadDefinition definition)
    {
        foreach (OwnerDefinition owner in definition.Owners)
        {
            owner.ThreadId = Environment.CurrentManagedThreadId;
        }

        ThreadLoopOptions options = definition.Options;
        try
        {
            while (true)
            {
                lock (Sync)
                {
                    if (definition.Exit)
                    {
                        return;
                    }
                }

                foreach (UpdateDefinition update in definition.Updates)
                {
                    RunUpdate(update, options);
                }

                long nextUpdate = long.MaxValue;
                lock (Sync)
                {
                    if (_status == ExecutionStatus.Running)
                    {
                        foreach (UpdateDefinition update in definition.Updates)
                        {
                            if (update.Gates.All(static gate => gate.Open && gate.Failure is null))
                            {
                                nextUpdate = Math.Min(nextUpdate, update.Next);
                            }
                        }
                    }
                }

                // A bounded turn yields back to due updates, not to the next update period.
                if (DrainGroups(definition.Groups, ref definition.Cursor, options.MaxControlWorkItems, options.MaxControlTime, false, ReadSequence(), nextUpdate) != 0)
                {
                    continue;
                }

                bool ready = false;
                lock (Sync)
                {
                    if (definition.Exit)
                    {
                        return;
                    }

                    foreach (GroupDefinition group in definition.Groups)
                    {
                        if (group.Head is { } candidate && CanClaim(candidate, out _))
                        {
                            ready = true;
                            break;
                        }
                    }
                }

                if (ready)
                {
                    continue;
                }

                int milliseconds = Timeout.Infinite;
                if (nextUpdate != long.MaxValue)
                {
                    TimeSpan remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), nextUpdate);
                    if (remaining <= TimeSpan.Zero)
                    {
                        continue;
                    }

                    milliseconds = (int)Math.Clamp(Math.Ceiling(remaining.TotalMilliseconds), 1, int.MaxValue);
                }

                // Queue admission, gate changes and shutdown all signal this event.
                definition.Wake!.WaitOne(milliseconds);
            }
        }
        catch (Exception error)
        {
            RecordFailure(error);
        }
    }

    // Worker reservations are synchronized by the runtime; this item holds no per-invocation state.
    private sealed class CpuWorker(ExecutionRuntime runtime) : IThreadPoolWorkItem
    {
        public void Execute() => runtime.RunWorker();
    }
}
