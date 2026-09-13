using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Hosting;

internal sealed class GraphicsPresentationGate(IGpuGraphPresentation presentation) : IGpuGraphPresentation
{
    private readonly object gate = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closed;
    private int acquiring;

    public void Close()
    {
        lock (gate)
        {
            closed = true;
            if (acquiring == 0)
            {
                drained.TrySetResult();
            }
        }
    }

    public Task DrainAsync() => drained.Task;

    public ValueTask<GpuGraphPresentationTarget> AcquireNextTargetAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            acquiring++;
        }
        return AcquireAsync(cancellationToken);
    }

    private async ValueTask<GpuGraphPresentationTarget> AcquireAsync(CancellationToken cancellationToken)
    {
        try
        {
            GpuGraphPresentationTarget target = await presentation.AcquireNextTargetAsync(cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                if (closed)
                {
                    presentation.Discard(target);
                    throw new ObjectDisposedException(nameof(GraphicsPresentationGate));
                }
            }
            return target;
        }
        finally
        {
            lock (gate)
            {
                acquiring--;
                if (closed && acquiring == 0)
                {
                    drained.TrySetResult();
                }
            }
        }
    }

    public void Present(GpuGraphPresentationTarget target, GpuGraphCompletion completion) => presentation.Present(target, completion);
    public void Retire(GpuGraphPresentationTarget target, GpuGraphCompletion completion) => presentation.Retire(target, completion);
    public void Discard(GpuGraphPresentationTarget target) => presentation.Discard(target);
}
