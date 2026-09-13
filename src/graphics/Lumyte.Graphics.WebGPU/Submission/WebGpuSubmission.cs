using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU;

/// <summary>Shared source for native and browser hosts; binds observation before an uncertain runtime handoff.</summary>
internal sealed class WebGpuSubmission(P.GpuFenceValue completion)
{
    private readonly TaskCompletionSource gpuEnded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<IReadOnlyList<P.GpuDiagnostic>> diagnostics =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task GpuEnded => gpuEnded.Task;
    internal Task<IReadOnlyList<P.GpuDiagnostic>> Diagnostics => diagnostics.Task;
    internal bool HandoffStarted { get; private set; }

    internal void ReleaseUnsubmitted(Action release)
    {
        if (!HandoffStarted) { release(); }
    }

    internal void Execute(Action accept, Action handoff, Func<Task> observeGpu,
        Func<Task<IReadOnlyList<P.GpuDiagnostic>>> observeDiagnostics)
    {
        // This can still fail without giving the runtime any command buffers.
        try { accept(); }
        catch (Exception acceptanceFailure)
        {
            // The caller has already pushed submission error scopes. Close them even if
            // the timeline cannot be bound, without handing off or observing GPU work.
            try { _ = ObserveDiagnosticsAsync(observeDiagnostics()); }
            catch (Exception scopeFailure)
            {
                diagnostics.TrySetException(scopeFailure);
                _ = ObserveRejectedDiagnosticsAsync();
                throw new AggregateException(acceptanceFailure, scopeFailure);
            }
            _ = ObserveRejectedDiagnosticsAsync();
            throw;
        }
        HandoffStarted = true;
        Exception? failure = null;
        try { handoff(); }
        catch (Exception error) { failure = error; }

        // A failed handoff or diagnostic hookup does not justify abandoning an independent
        // opportunity to observe the GPU. A faulted observation is never successful completion.
        try { _ = ObserveGpuAsync(observeGpu()); }
        catch (Exception error)
        {
            gpuEnded.TrySetException(error);
            failure = Combine(failure, error);
        }
        try { _ = ObserveDiagnosticsAsync(observeDiagnostics()); }
        catch (Exception error)
        {
            diagnostics.TrySetException(error);
            failure = Combine(failure, error);
        }

        if (failure is not null) { throw new P.GpuSubmissionException(completion, failure); }
    }

    private async Task ObserveGpuAsync(Task observation)
    {
        try { await observation.ConfigureAwait(false); gpuEnded.TrySetResult(); }
        catch (Exception error) { gpuEnded.TrySetException(error); }
    }

    private async Task ObserveDiagnosticsAsync(Task<IReadOnlyList<P.GpuDiagnostic>> observation)
    {
        try { diagnostics.TrySetResult(await observation.ConfigureAwait(false)); }
        catch (Exception error) { diagnostics.TrySetException(error); }
    }

    private async Task ObserveRejectedDiagnosticsAsync()
    {
        // No timeline owns the observation when acceptance failed. Observe any later fault
        // without replacing the synchronous failure already being returned to the caller.
        try { await diagnostics.Task.ConfigureAwait(false); }
        catch { }
    }

    private static Exception Combine(Exception? previous, Exception current)
        => previous is null ? current : new AggregateException(previous, current);
}
