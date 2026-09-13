using System.Numerics;
using Lumyte.Graphics.Passes;

namespace Lumyte.Graphics.RenderGraph.Conformance;

/// <summary>Offscreen presentation for the same compiled consumer on every provider, without a window dependency.</summary>
public sealed class HeadlessImagePresentation : IGpuGraphPresentation, IAsyncDisposable
{
    private readonly IGpuRenderRuntime runtime;
    private readonly GpuGraphResourcePin ownership;
    private readonly object gate = new();
    private readonly SemaphoreSlim available = new(1, 1);
    private readonly CancellationTokenSource closeWaiters = new();
    private readonly Dictionary<GpuGraphPresentationTarget, bool> targets = [];
    private readonly List<Exception> failures = [];
    private readonly TaskCompletionSource failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? returned;
    private bool closing;
    private bool disposed;
    private HeadlessImagePresentation(IGpuRenderRuntime runtime, GpuGraphTextureRef texture, GpuGraphResourcePin ownership)
    { this.runtime = runtime; Texture = texture; this.ownership = ownership; }
    public GpuGraphTextureRef Texture { get; }
    public int AcquiredCount { get; private set; }
    public int PresentedCount { get; private set; }
    public int DiscardedCount { get; private set; }

    public static async ValueTask<HeadlessImagePresentation> CreateAsync(IGpuRenderRuntime runtime,
        GpuGraphTextureDescription description, CancellationToken cancellationToken = default)
    {
        ImagePipelinePlan bootstrap = ImagePipelineConsumer.CreateExportPlan(description, TextureClearValue.Color(Vector4.Zero));
        using GpuRenderGraphExecution execution = await runtime.SubmitAsync(bootstrap.Plan, cancellationToken: cancellationToken);
        await execution.WaitForCompletionAsync(cancellationToken);
        GpuGraphTextureRef texture = execution.GetExportedTexture(bootstrap.Output);
        return new(runtime, texture, runtime.Resources.Pin(texture));
    }
    public async ValueTask<GpuGraphPresentationTarget> AcquireNextTargetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate) { ObjectDisposedException.ThrowIf(closing, this); }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, closeWaiters.Token);
        await available.WaitAsync(cancellation.Token).ConfigureAwait(false);
        try
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(closing, this);
                var target = new GpuGraphPresentationTarget(Texture, runtime.Resources.Pin(Texture));
                returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
                targets.Add(target, false); AcquiredCount++;
                return target;
            }
        }
        catch { available.Release(); throw; }
    }
    public void Present(GpuGraphPresentationTarget target, GpuGraphCompletion completion)
    { lock (gate) { BeginReturn(target, completion); PresentedCount++; } }
    public void Retire(GpuGraphPresentationTarget target, GpuGraphCompletion completion)
    { lock (gate) { BeginReturn(target, completion); } }
    private void BeginReturn(GpuGraphPresentationTarget target, GpuGraphCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        RequireAcquired(target); targets[target] = true;
        _ = ReturnAsync(target, completion);
    }
    private void RequireAcquired(GpuGraphPresentationTarget target)
    {
        if (!targets.TryGetValue(target, out bool isReturning) || isReturning)
        { throw new ArgumentException("The target was not acquired from this presentation or has already been returned.", nameof(target)); }
    }
    private void FinishReturn(GpuGraphPresentationTarget target)
    {
        target.Ownership.Dispose();
        targets.Remove(target); returned!.TrySetResult(); available.Release();
    }
    private async Task ReturnAsync(GpuGraphPresentationTarget target, GpuGraphCompletion completion)
    {
        try
        {
            try { await completion.WaitAsync().ConfigureAwait(false); }
            catch when (completion.IsComplete) { /* Failed pixels can still have ended all GPU use. */ }
            lock (gate) { FinishReturn(target); }
        }
        catch (Exception error)
        {
            // Keep the acquired target and its pin when GPU use or release remains uncertain.
            lock (gate) { failures.Add(error); failed.TrySetResult(); }
        }
    }
    public void Discard(GpuGraphPresentationTarget target)
    {
        lock (gate)
        {
            RequireAcquired(target); targets[target] = true;
            try { FinishReturn(target); DiscardedCount++; }
            catch (Exception error) { failures.Add(error); failed.TrySetResult(); throw; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        Task drain;
        lock (gate)
        {
            if (disposed) { return; } closing = true;
            drain = targets.Count == 0 ? Task.CompletedTask : returned!.Task;
        }
        closeWaiters.Cancel();
        await Task.WhenAny(drain, failed.Task).ConfigureAwait(false);
        lock (gate)
        {
            if (failures.Count != 0) { throw new AggregateException("Presentation use did not end with certainty.", failures); }
            if (disposed) { return; }
            ownership.Dispose(); disposed = true;
        }
    }
}
