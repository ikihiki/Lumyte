using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU;

/// <summary>Tracks accepted queue points, GPU use ending, and independent submission diagnostics.</summary>
internal sealed class WebGpuTimeline(object queueIdentity, WebGpuDeviceStatus status, ulong initialValue) : P.GpuSemaphore
{
    private sealed class PendingPoint
    {
        internal bool Accepted;
        internal Task? GpuEnded;
        internal readonly TaskCompletionSource Settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly record struct Interval(ulong First, ulong Last);
    private readonly object gate = new();
    private readonly Dictionary<ulong, PendingPoint> pending = new();
    private readonly List<Interval> successes = [];
    private readonly Dictionary<ulong, IReadOnlyList<P.GpuDiagnostic>> failures = new();
    private readonly ulong initial = initialValue;
    private ulong reservedValue = initialValue;
    private ulong gpuEndedValue = initialValue;
    private bool disposed;

    internal void VerifyOwner(object owner)
    {
        lock (gate)
        {
            RequireAvailable();
            if (!ReferenceEquals(queueIdentity, owner)) { throw new ArgumentException("Timeline belongs to another queue.", nameof(owner)); }
        }
    }

    internal void ValidateSignal(ulong value)
    {
        lock (gate) { RequireAvailable(); RequireIncreasingSignal(value); }
    }

    internal void Reserve(ulong value)
    {
        lock (gate)
        {
            RequireAvailable();
            RequireIncreasingSignal(value);
            pending.Add(value, new PendingPoint());
            reservedValue = value;
        }
    }

    internal void Accept(ulong value, Task gpuEnded, Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(gpuEnded);
        ArgumentNullException.ThrowIfNull(diagnostics);
        PendingPoint point;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!pending.TryGetValue(value, out point!) || point.Accepted)
            { throw new InvalidOperationException("The signal value must be reserved exactly once before acceptance."); }
            point.GpuEnded = gpuEnded;
            point.Accepted = true;
        }
        _ = ObserveSubmissionAsync(value, point, gpuEnded, diagnostics);
    }

    internal bool IsComplete(ulong value)
    {
        lock (gate)
        {
            RequireAvailable();
            PendingPoint? point = RequireAcceptedPoint(value);
            if (point?.GpuEnded?.IsCompletedSuccessfully == true) { gpuEndedValue = Math.Max(gpuEndedValue, value); }
            return value <= gpuEndedValue;
        }
    }

    internal ValueTask WaitAsync(ulong value, CancellationToken cancellationToken = default)
    {
        Task settled;
        lock (gate)
        {
            RequireAvailable();
            settled = RequireAcceptedPoint(value)?.Settled.Task ?? Task.CompletedTask;
        }
        return new(WaitForResultAsync(value, settled, cancellationToken));
    }

    private async Task WaitForResultAsync(ulong value, Task settled, CancellationToken cancellationToken)
    {
        await Task.WhenAny(settled, status.Failure).WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            RequireAvailable();
            if (failures.TryGetValue(value, out IReadOnlyList<P.GpuDiagnostic>? diagnostics))
            { throw new P.GpuExecutionException(new(this, value), diagnostics); }
        }
    }

    private async Task ObserveSubmissionAsync(ulong value, PendingPoint point, Task gpuEnded,
        Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics)
    {
        try
        {
            Task gpu = ObserveGpuAsync(value, gpuEnded);
            Task<IReadOnlyList<P.GpuDiagnostic>?> diagnosticResult = ObserveDiagnosticsAsync(diagnostics);
            await Task.WhenAll(gpu, diagnosticResult).ConfigureAwait(false);
            IReadOnlyList<P.GpuDiagnostic>? result = await diagnosticResult.ConfigureAwait(false);
            lock (gate)
            {
                if (disposed || status.Failure.IsCompleted || result is null) { return; }
                if (result.Count == 0) { AddSuccess(value); }
                else { failures.Add(value, result); }
                pending.Remove(value);
                point.Settled.TrySetResult();
            }
        }
        catch (Exception error) { status.Lose($"WebGPU submission observation failed: {error.Message}"); }
    }

    private async Task ObserveGpuAsync(ulong value, Task gpuEnded)
    {
        try
        {
            await Task.WhenAny(gpuEnded, status.Failure).ConfigureAwait(false);
            if (status.Failure.IsCompleted) { return; }
            await gpuEnded.ConfigureAwait(false);
            lock (gate) { gpuEndedValue = Math.Max(gpuEndedValue, value); }
        }
        catch (Exception error) { status.Lose($"WebGPU queue completion failed: {error.Message}"); }
    }

    private async Task<IReadOnlyList<P.GpuDiagnostic>?> ObserveDiagnosticsAsync(Task<IReadOnlyList<P.GpuDiagnostic>> diagnostics)
    {
        try
        {
            await Task.WhenAny(diagnostics, status.Failure).ConfigureAwait(false);
            if (status.Failure.IsCompleted) { return null; }
            IReadOnlyList<P.GpuDiagnostic> result = await diagnostics.ConfigureAwait(false);
            return Array.AsReadOnly(result.ToArray());
        }
        catch (Exception error)
        {
            status.Lose($"WebGPU submission diagnostics failed: {error.Message}");
            return null;
        }
    }

    private PendingPoint? RequireAcceptedPoint(ulong value)
    {
        if (value == initial || failures.ContainsKey(value) || ContainsSuccess(value)) { return null; }
        if (pending.TryGetValue(value, out PendingPoint? point) && point.Accepted) { return point; }
        throw new ArgumentOutOfRangeException(nameof(value), "Only the initial value and accepted signal values can be observed.");
    }

    private void RequireIncreasingSignal(ulong value)
    {
        if (value <= reservedValue) { throw new ArgumentOutOfRangeException(nameof(value), "Signal values must exceed every previously reserved value."); }
    }

    private void RequireAvailable()
    {
        status.ThrowIfFailed();
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private bool ContainsSuccess(ulong value)
    {
        int low = 0;
        int high = successes.Count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            Interval interval = successes[middle];
            if (value < interval.First) { high = middle - 1; }
            else if (value > interval.Last) { low = middle + 1; }
            else { return true; }
        }
        return false;
    }

    private void AddSuccess(ulong value)
    {
        int low = 0;
        int high = successes.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (successes[middle].First < value) { low = middle + 1; }
            else { high = middle; }
        }
        int index = low;
        bool previous = index > 0 && successes[index - 1].Last != ulong.MaxValue && successes[index - 1].Last + 1 == value;
        bool next = index < successes.Count && value != ulong.MaxValue && value + 1 == successes[index].First;
        if (previous)
        {
            successes[index - 1] = new(successes[index - 1].First, next ? successes[index].Last : value);
            if (next) { successes.RemoveAt(index); }
        }
        else if (next) { successes[index] = new(value, successes[index].Last); }
        else { successes.Insert(index, new(value, value)); }
    }

    internal (int PendingBatches, int SuccessfulIntervals, int FailedBatches) Statistics
    {
        get { lock (gate) { return (pending.Count, successes.Count, failures.Count); } }
    }

    public override void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
            foreach (PendingPoint point in pending.Values) { point.Settled.TrySetResult(); }
            pending.Clear();
            successes.Clear();
            failures.Clear();
        }
    }
}
