using System.Diagnostics;

namespace Lumyte.Graphics.Abstractions;

internal sealed class GraphicsDeviceLifetime(object owner)
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource<GraphicsDeviceLoss> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GraphicsDeviceStatus _status;
    private GraphicsDeviceLoss? _loss;

    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost;

    public GraphicsDeviceStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public GraphicsDeviceLoss? Loss
    {
        get
        {
            lock (_gate)
            {
                return _loss;
            }
        }
    }

    internal Task<GraphicsDeviceLoss> LostTask => _lost.Task;

    internal void ReportLoss(string reason, string message)
    {
        var loss = new GraphicsDeviceLoss(reason, message);
        lock (_gate)
        {
            if (_status != GraphicsDeviceStatus.Ready)
            {
                return;
            }

            _loss = loss;
            _status = GraphicsDeviceStatus.Lost;
            _lost.TrySetResult(loss);
        }

        foreach (Delegate handler in DeviceLost?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<GraphicsDeviceLostEventArgs>)handler)(owner, new(loss));
            }
            catch (Exception error)
            {
                Trace.TraceError("Device loss subscriber failed: {0}", error);
            }
        }
    }

    internal void Dispose()
    {
        lock (_gate)
        {
            _status = GraphicsDeviceStatus.Disposed;
        }
    }
}
