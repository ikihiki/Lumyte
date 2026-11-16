using System.Diagnostics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Maintains the cached device status and publishes a native device-loss observation once.</summary>
/// <param name="owner">The device instance supplied as the event sender.</param>
public sealed class GraphicsDeviceLifetime(object owner)
{
    private readonly object _gate = new();
    private readonly TaskCompletionSource<GraphicsDeviceLoss> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GraphicsDeviceStatus _status;
    private GraphicsDeviceLoss? _loss;

    /// <summary>Occurs when the device first transitions from ready to lost.</summary>
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost;

    /// <summary>Gets the cached device status without querying the native device.</summary>
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

    /// <summary>Gets the first reported device loss, or <see langword="null"/> while none has been reported.</summary>
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

    /// <summary>Gets a task completed by the first reported device loss.</summary>
    public Task<GraphicsDeviceLoss> LostTask => _lost.Task;

    /// <summary>Records and publishes the first native device-loss observation.</summary>
    /// <param name="reason">The backend-specific loss reason.</param>
    /// <param name="message">The backend-specific diagnostic message.</param>
    public void ReportLoss(string reason, string message) => ReportLoss(reason, message, false);

    /// <summary>Marks the device as disposed.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _status = GraphicsDeviceStatus.Disposed;
        }
    }

    internal void ReportLossDeferred(string reason, string message) => ReportLoss(reason, message, true);

    private void ReportLoss(string reason, string message, bool deferNotification)
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

        Delegate[] handlers = DeviceLost?.GetInvocationList() ?? [];
        if (deferNotification)
        {
            ThreadPool.QueueUserWorkItem(_ => NotifyLoss(loss, handlers));
        }
        else
        {
            NotifyLoss(loss, handlers);
        }
    }

    private void NotifyLoss(GraphicsDeviceLoss loss, Delegate[] handlers)
    {
        foreach (Delegate handler in handlers)
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
}
