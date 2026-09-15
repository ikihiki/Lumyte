namespace Lumyte.Graphics.RenderGraph;

/// <summary>Serial surface ownership, asynchronous GPU retirement and bounded frame pacing.</summary>
/// <remarks>The application closes this connection before destroying its window/canvas and device.
/// Unobservable GPU use retains the acquired target and causes shutdown to fail.</remarks>
public abstract class GpuSurfacePresentation : IGpuGraphPresentation, IAsyncDisposable
{
    private readonly SemaphoreSlim available = new(1, 1);
    private readonly CancellationTokenSource stopping = new();
    private readonly object gate = new();
    private GpuGraphPresentationTarget? acquired;
    private bool returning, closing;
    private Task pending = Task.CompletedTask;
    private Exception? failure;
    private Task? disposal;
    private int acquiring;
    private TaskCompletionSource? acquireEnded;
    protected abstract ValueTask<GpuGraphPresentationTarget> AcquireCoreAsync(CancellationToken cancellationToken);
    protected abstract ValueTask ReturnCoreAsync(GpuGraphPresentationTarget target, bool present);
    protected abstract ValueTask DisposeCoreAsync();
    public async ValueTask<GpuGraphPresentationTarget> AcquireNextTargetAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        { ObjectDisposedException.ThrowIf(closing, this); ThrowFailure(); acquiring++; acquireEnded ??= new(TaskCreationOptions.RunContinuationsAsynchronously); }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
        bool taken = false;
        try
        {
            await available.WaitAsync(linked.Token).ConfigureAwait(false);
            taken = true;
            lock (gate)
            { ObjectDisposedException.ThrowIf(closing, this); ThrowFailure(); }
            var target = await AcquireCoreAsync(linked.Token).ConfigureAwait(false);
            bool reject;
            lock (gate)
            { reject = closing || linked.IsCancellationRequested; if (!reject) { acquired = target; returning = false; } }
            if (reject)
            {
                try
                { await ReturnCoreAsync(target, false).ConfigureAwait(false); }
                catch (Exception error)
                {
                    lock (gate)
                    { acquired = target; returning = true; failure = error; }
                    taken = false;
                    stopping.Cancel();
                    throw;
                }
                throw new OperationCanceledException(linked.Token);
            }
            return target;
        }
        catch { if (taken) { available.Release(); } throw; }
        finally { lock (gate) { if (--acquiring == 0) { acquireEnded!.TrySetResult(); acquireEnded = null; } } }
    }
    public void Present(GpuGraphPresentationTarget target, GpuGraphCompletion completion) => BeginReturn(target, completion, true);
    public void Retire(GpuGraphPresentationTarget target, GpuGraphCompletion completion) => BeginReturn(target, completion, false);
    private void BeginReturn(GpuGraphPresentationTarget target, GpuGraphCompletion completion, bool present)
    {
        ArgumentNullException.ThrowIfNull(completion);
        lock (gate)
        { RequireTarget(target); returning = true; pending = ReturnAsync(target, completion, present); }
    }
    private async Task ReturnAsync(GpuGraphPresentationTarget target, GpuGraphCompletion? completion, bool present)
    {
        try
        {
            if (completion is not null)
            {
                try
                { await completion.WaitAsync().ConfigureAwait(false); }
                catch when (completion.IsComplete) { present = false; }
            }
            await ReturnCoreAsync(target, present).ConfigureAwait(false);
            lock (gate)
            { acquired = null; returning = false; available.Release(); }
        }
        catch (Exception error) { lock (gate) { failure = error; } stopping.Cancel(); }
    }
    public void Discard(GpuGraphPresentationTarget target)
    {
        lock (gate)
        { RequireTarget(target); returning = true; pending = ReturnAsync(target, null, false); }
    }
    private void RequireTarget(GpuGraphPresentationTarget target)
    { if (!ReferenceEquals(acquired, target) || returning) { throw new ArgumentException("Target was not acquired here or has already been returned.", nameof(target)); } }
    private void ThrowFailure() { if (failure is not null) { throw new InvalidOperationException("Presentation failed; surface ownership is retained until safe shutdown.", failure); } }
    /// <summary>Waits for the latest presentation/retirement and reports asynchronous surface failures.</summary>
    public async Task WaitForPresentationAsync(CancellationToken cancellationToken = default)
    {
        Task current;
        lock (gate)
        { current = pending; }
        await current.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (gate)
        { ThrowFailure(); }
    }
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is not null)
            { return new(disposal); }
            if (acquired is not null && !returning)
            { throw new InvalidOperationException("Return the unsubmitted frame before closing presentation."); }
            closing = true;
            stopping.Cancel();
            disposal = DisposeAfterAsync(acquireEnded?.Task ?? Task.CompletedTask, pending);
            return new(disposal);
        }
    }
    private async Task DisposeAfterAsync(Task acquisitions, Task submitted)
    {
        await acquisitions.ConfigureAwait(false);
        await submitted.ConfigureAwait(false);
        lock (gate)
        { ThrowFailure(); }
        await DisposeCoreAsync().ConfigureAwait(false);
    }
}
