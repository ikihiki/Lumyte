using System.Diagnostics;

namespace Lumyte.Resources;

internal sealed class ResourceLoadScheduler
{
    private readonly Lock gate = new();
    private readonly int maxConcurrentLoads;
    private readonly IReadOnlyDictionary<ResourceExecutionLane, int> laneLimits;
    private readonly Dictionary<ResourceExecutionLane, int> activeByLane = [];
    private readonly List<WorkItem> pending = [];
    private long sequence;
    private int active;

    internal ResourceLoadScheduler(ResourceSchedulingOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxConcurrentLoads, 1);
        maxConcurrentLoads = options.MaxConcurrentLoads;
        laneLimits = new Dictionary<ResourceExecutionLane, int>(
            options.MaxConcurrentLoadsPerLane);
        foreach ((ResourceExecutionLane lane, int limit) in laneLimits)
        {
            if (string.IsNullOrWhiteSpace(lane.Name) || limit < 1)
            {
                throw new ArgumentException(
                    "Resource scheduling lanes require a name and a positive limit.",
                    nameof(options));
            }
        }
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
            this);
        lock (gate)
        {
            pending.Add(item);
            ResourcesDiagnostics.QueuedLoads.Add(
                1,
                new("lane", lane.Name),
                new("priority", priority.ToString()));
            Pump();
        }

        return new ValueTask<T>(item.Task);
    }

    internal void Promote(uint slot, ResourceLoadPriority priority)
    {
        lock (gate)
        {
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

    private void Pump()
    {
        while (active < maxConcurrentLoads)
        {
            WorkItem? next = pending
                .Where(CanRun)
                .OrderByDescending(item => item.Priority)
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
                Stopwatch.GetElapsedTime(next.EnqueuedAt).TotalMilliseconds,
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

    private abstract class WorkItem(
        uint slot,
        ResourceExecutionLane lane,
        ResourceLoadPriority priority,
        long sequence)
    {
        internal uint Slot { get; } = slot;

        internal ResourceExecutionLane Lane { get; } = lane;

        internal ResourceLoadPriority Priority { get; set; } = priority;

        internal long Sequence { get; } = sequence;

        internal long EnqueuedAt { get; } = Stopwatch.GetTimestamp();

        internal abstract void Start();
    }

    private sealed class WorkItem<T>(
        uint slot,
        ResourceExecutionLane lane,
        ResourceLoadPriority priority,
        long sequence,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken,
        ResourceLoadScheduler owner)
        : WorkItem(slot, lane, priority, sequence)
    {
        private readonly TaskCompletionSource<T> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task<T> Task => completion.Task;

        internal override void Start() => _ = RunAsync();

        private async Task RunAsync()
        {
            try
            {
                completion.TrySetResult(
                    await operation(cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                owner.Complete(this);
            }
        }
    }
}
