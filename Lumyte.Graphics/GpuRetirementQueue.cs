namespace Lumyte.Graphics;

/// <summary>Identifies one submission owned by a <see cref="GpuRetirementQueue"/>.</summary>
public readonly struct GpuSubmissionToken : IEquatable<GpuSubmissionToken>
{
    private readonly GpuRetirementQueue? owner;

    internal GpuSubmissionToken(GpuRetirementQueue owner, ulong value)
    {
        this.owner = owner;
        Value = value;
    }

    public ulong Value { get; }
    public bool IsValid => owner is not null;
    public bool IsComplete => owner is null || owner.IsComplete(this);

    public void Wait() => owner?.Wait(this);

    public bool Equals(GpuSubmissionToken other) =>
        ReferenceEquals(owner, other.owner) && Value == other.Value;

    public override bool Equals(object? obj) => obj is GpuSubmissionToken other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(owner, Value);
    public static bool operator ==(GpuSubmissionToken left, GpuSubmissionToken right) => left.Equals(right);
    public static bool operator !=(GpuSubmissionToken left, GpuSubmissionToken right) => !left.Equals(right);
}

/// <summary>
/// Tracks GPU submissions and runs deferred release actions only after their semaphore value completes.
/// Call <see cref="Collect"/> once per frame, or use <see cref="WaitIdle"/> during shutdown.
/// </summary>
public sealed class GpuRetirementQueue : IDisposable
{
    private readonly IGpuBackend backend;
    private readonly IGpuQueue queue;
    private readonly GpuSemaphore semaphore;
    private readonly SortedSet<ulong> inFlight = [];
    private readonly SortedDictionary<ulong, List<Action>> retirements = [];
    private ulong nextValue;
    private ulong completedValue;
    private bool disposed;

    public GpuRetirementQueue(IGpuBackend backend, int maximumFramesInFlight = 3)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if (maximumFramesInFlight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFramesInFlight));
        }

        MaximumFramesInFlight = maximumFramesInFlight;
        queue = backend.MainQueue;
        semaphore = queue.CreateSemaphore();
    }

    public int MaximumFramesInFlight { get; }
    public int InFlightSubmissionCount => inFlight.Count;
    public GpuFenceValue CompletedFence => new(completedValue);

    /// <summary>Polls the oldest submissions and releases everything that has completed.</summary>
    public int Collect()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        int completed = 0;
        while (inFlight.Count != 0)
        {
            ulong value = inFlight.Min;
            if (!queue.IsComplete(semaphore, value)) { break; }
            CompleteThrough(value);
            completed++;
        }
        return completed;
    }

    public void WaitIdle()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (inFlight.Count == 0) { return; }
        ulong value = inFlight.Max;
        queue.Wait(semaphore, value);
        CompleteThrough(value);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        WaitIdle();
        semaphore.Dispose();
        disposed = true;
    }

    internal void RequireBackend(IGpuBackend candidate)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(backend, candidate))
        {
            throw new ArgumentException(
                "Retirement queue belongs to another GPU backend.",
                nameof(candidate));
        }
    }

    internal GpuSubmissionToken Submit(
        GpuCommandBuffer commands,
        IReadOnlyList<Action> completionActions)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(completionActions);
        Collect();
        if (inFlight.Count >= MaximumFramesInFlight)
        {
            ulong oldest = inFlight.Min;
            queue.Wait(semaphore, oldest);
            CompleteThrough(oldest);
        }
        ulong value = checked(nextValue + 1);
        if (completionActions.Count != 0)
        {
            retirements.Add(value, [.. completionActions]);
        }
        try
        {
            queue.Submit([commands], semaphore, value);
            nextValue = value;
            inFlight.Add(value);
            return new(this, value);
        }
        catch
        {
            retirements.Remove(value);
            throw;
        }
    }

    internal void Retire(GpuSubmissionToken token, Action release)
        => Retire(token, [release]);

    internal void Retire(GpuSubmissionToken token, IReadOnlyList<Action> releases)
    {
        ArgumentNullException.ThrowIfNull(releases);
        RequireToken(token, allowDisposed: true);
        if (token.Value <= completedValue || disposed)
        {
            RunActions(releases);
            return;
        }

        if (!retirements.TryGetValue(token.Value, out List<Action>? actions))
        {
            actions = [];
            retirements.Add(token.Value, actions);
        }
        actions.AddRange(releases);
    }

    internal bool IsComplete(GpuSubmissionToken token)
    {
        RequireToken(token, allowDisposed: true);
        if (token.Value <= completedValue) { return true; }
        if (disposed) { return true; }
        Collect();
        return token.Value <= completedValue;
    }

    internal void Wait(GpuSubmissionToken token)
    {
        RequireToken(token, allowDisposed: true);
        if (token.Value <= completedValue || disposed) { return; }
        queue.Wait(semaphore, token.Value);
        CompleteThrough(token.Value);
    }

    private void CompleteThrough(ulong value)
    {
        foreach (ulong completed in inFlight.TakeWhile(candidate => candidate <= value).ToArray())
        {
            inFlight.Remove(completed);
        }

        List<Action> ready = [];
        foreach (ulong fence in retirements.Keys.TakeWhile(candidate => candidate <= value).ToArray())
        {
            ready.AddRange(retirements[fence]);
            retirements.Remove(fence);
        }
        completedValue = Math.Max(completedValue, value);
        RunActions(ready);
    }

    private static void RunActions(IReadOnlyList<Action> actions)
    {
        List<Exception>? failures = null;
        foreach (Action release in actions)
        {
            try { release(); }
            catch (Exception exception)
            {
                failures ??= [];
                failures.Add(exception);
            }
        }
        if (failures is not null) { throw new AggregateException(failures); }
    }

    private void RequireToken(GpuSubmissionToken token, bool allowDisposed = false)
    {
        if (!token.IsValid || token != new GpuSubmissionToken(this, token.Value))
        {
            throw new ArgumentException(
                "Submission token belongs to another retirement queue.",
                nameof(token));
        }
        if (!allowDisposed) { ObjectDisposedException.ThrowIf(disposed, this); }
        if (token.Value > nextValue)
        {
            throw new ArgumentOutOfRangeException(nameof(token));
        }
    }
}
