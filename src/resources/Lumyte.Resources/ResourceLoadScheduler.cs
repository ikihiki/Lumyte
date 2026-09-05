using System.Diagnostics;

namespace Lumyte.Resources;

internal sealed class ResourceLoadScheduler
{
    private readonly Lock gate = new();
    private readonly int maxConcurrentLoads;
    private readonly IReadOnlyDictionary<ResourceExecutionLane, int> laneLimits;
    private readonly IReadOnlyDictionary<ResourceExecutionLane, SemaphoreSlim> laneGates;
    private readonly TimeSpan agingInterval;
    private readonly TimeProvider timeProvider;
    private readonly Dictionary<ResourceExecutionLane, int> activeByLane = [];
    private readonly Dictionary<uint, ResourceLoadPriority> priorityHints = [];
    private readonly List<WorkItem> pending = [];
    private long sequence;
    private int active;

    internal ResourceLoadScheduler(ResourceSchedulingOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConcurrentLoads, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.AgingInterval, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        maxConcurrentLoads = options.MaxConcurrentLoads;
        agingInterval = options.AgingInterval;
        timeProvider = options.TimeProvider;
        laneLimits = new Dictionary<ResourceExecutionLane, int>(
            options.MaxConcurrentLoadsPerLane);
        var configuredLaneGates = new Dictionary<ResourceExecutionLane, SemaphoreSlim>();
        foreach ((ResourceExecutionLane lane, int limit) in options.MaxConcurrentLoadsPerLane)
        {
            if (string.IsNullOrWhiteSpace(lane.Name) || limit < 1)
            {
                throw new ArgumentException(
                    "Resource scheduling lanes require a name and a positive limit.",
                    nameof(options));
            }

            configuredLaneGates.Add(lane, new SemaphoreSlim(limit, limit));
        }


        laneGates = configuredLaneGates;
    }

    internal ValueTask<T> ScheduleAsync<T>(
        uint slot,
        ResourceExecutionLane lane,
        ResourceLoadPriority priority,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = new WorkItem<T>(
            slot,
            lane,
            priority,
            Interlocked.Increment(ref sequence),
            operation,
            cancellationToken,
            timeProvider.GetTimestamp(),
            this);
        lock (gate)
        {
            if (priorityHints.TryGetValue(slot, out ResourceLoadPriority hint)
                && hint > item.Priority)
            {
                item.Priority = hint;
            }

            pending.Add(item);
            ResourcesDiagnostics.QueuedLoads.Add(
                1,
                new("lane", lane.Name),
                new("priority", priority.ToString()));
            item.RegisterCancellation();
            Pump();
        }

        return new ValueTask<T>(item.Task);
    }

    internal void Promote(uint slot, ResourceLoadPriority priority)
    {
        lock (gate)
        {
            if (!priorityHints.TryGetValue(slot, out ResourceLoadPriority current)
                || current < priority)
            {
                priorityHints[slot] = priority;
            }

            foreach (WorkItem item in pending)
            {
                if (item.Slot == slot && item.Priority < priority)
                {
                    item.Priority = priority;
                }
            }

            Pump();
        }
    }

    internal void CompleteRequest(uint slot)
    {
        lock (gate)
        {
            priorityHints.Remove(slot);
        }
    }

    internal async ValueTask<T> RunStageAsync<T>(
        ResourceExecutionLane lane,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken)
    {
        if (!laneGates.TryGetValue(lane, out SemaphoreSlim? laneGate))
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        await laneGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            laneGate.Release();
        }
    }

    private void Pump()
    {
        while (active < maxConcurrentLoads)
        {
            WorkItem? next = pending
                .Where(CanRun)
                .OrderByDescending(GetEffectivePriority)
                .ThenBy(item => item.Sequence)
                .FirstOrDefault();
            if (next is null)
            {
                return;
            }

            pending.Remove(next);
            ResourcesDiagnostics.QueuedLoads.Add(
                -1,
                new("lane", next.Lane.Name),
                new("priority", next.Priority.ToString()));
            ResourcesDiagnostics.SchedulingWaitDuration.Record(
                timeProvider.GetElapsedTime(next.EnqueuedAt).TotalMilliseconds,
                new("lane", next.Lane.Name),
                new("priority", next.Priority.ToString()));
            active++;
            activeByLane[next.Lane] = activeByLane.GetValueOrDefault(next.Lane) + 1;
            ResourcesDiagnostics.ScheduledLoads.Add(
                1,
                new KeyValuePair<string, object?>("lane", next.Lane.Name));
            next.Start();
        }
    }

    private int GetEffectivePriority(WorkItem item)
    {
        long boosts = timeProvider.GetElapsedTime(item.EnqueuedAt).Ticks
            / agingInterval.Ticks;
        return (int)Math.Min(
            (long)ResourceLoadPriority.Critical,
            (long)item.Priority + boosts);
    }

    private bool CanRun(WorkItem item) =>
        !laneLimits.TryGetValue(item.Lane, out int limit)
        || activeByLane.GetValueOrDefault(item.Lane) < limit;

    private void Complete(WorkItem item)
    {
        lock (gate)
        {
            active--;
            activeByLane[item.Lane]--;
            ResourcesDiagnostics.ScheduledLoads.Add(
                -1,
                new KeyValuePair<string, object?>("lane", item.Lane.Name));
            Pump();
        }
    }

    private void CancelPending(WorkItem item)
    {
        lock (gate)
        {
            if (!pending.Remove(item))
            {
                return;
            }

            ResourcesDiagnostics.QueuedLoads.Add(
                -1,
                new("lane", item.Lane.Name),
                new("priority", item.Priority.ToString()));
            item.SetCanceled();
            Pump();
        }
    }

    private abstract class WorkItem
    {
        protected WorkItem(
            uint slot,
            ResourceExecutionLane lane,
            ResourceLoadPriority priority,
            long sequence,
            CancellationToken cancellationToken,
            long enqueuedAt,
            ResourceLoadScheduler owner)
        {
            Slot = slot;
            Lane = lane;
            Priority = priority;
            Sequence = sequence;
            CancellationToken = cancellationToken;
            EnqueuedAt = enqueuedAt;
            Owner = owner;
        }

        internal uint Slot { get; }

        internal ResourceExecutionLane Lane { get; }

        internal ResourceLoadPriority Priority { get; set; }

        internal long Sequence { get; }

        private CancellationTokenRegistration cancellationRegistration;

        internal long EnqueuedAt { get; }

        protected CancellationToken CancellationToken { get; }

        protected ResourceLoadScheduler Owner { get; }

        internal void RegisterCancellation() => cancellationRegistration =
            CancellationToken.Register(
                static state =>
                {
                    WorkItem item = (WorkItem)state!;
                    item.Owner.CancelPending(item);
                },
                this);

        internal void DisposeCancellationRegistration() => cancellationRegistration.Dispose();

        internal abstract void Start();

        internal abstract void SetCanceled();

    }

    private sealed class WorkItem<T>(
        uint slot,
        ResourceExecutionLane lane,
        ResourceLoadPriority priority,
        long sequence,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken,
        long enqueuedAt,
        ResourceLoadScheduler owner)
        : WorkItem(
            slot,
            lane,
            priority,
            sequence,
            cancellationToken,
            enqueuedAt,
            owner)
    {
        private readonly TaskCompletionSource<T> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task<T> Task => completion.Task;

        internal override void Start()
        {
            DisposeCancellationRegistration();
            _ = RunAsync();
        }

        internal override void SetCanceled()
        {
            DisposeCancellationRegistration();
            completion.TrySetCanceled(CancellationToken);
        }

        private async Task RunAsync()
        {
            try
            {
                completion.TrySetResult(
                    await operation(CancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                Owner.Complete(this);
            }
        }
    }
}
