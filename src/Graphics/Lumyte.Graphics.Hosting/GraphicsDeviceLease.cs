using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Hosting;

/// <summary>Retains one exact device epoch without transferring native ownership.</summary>
public sealed class GraphicsDeviceLease : IDisposable
{
    private GraphicsDeviceService? _service;
    private GraphicsDeviceService.Entry? _entry;

    internal GraphicsDeviceLease(GraphicsDeviceService service, GraphicsDeviceService.Entry entry, GraphicsDeviceState state)
    {
        (_service, _entry, State) = (service, entry, state);
    }

    /// <summary>Gets the Ready state captured when this lease was acquired.</summary>
    public GraphicsDeviceState State { get; }

    /// <summary>Gets a value indicating whether the lease is retained and its physical device remains Ready.</summary>
    public bool IsUsable => Volatile.Read(ref _entry) is { } entry && entry.Device.Status == GraphicsDeviceStatus.Ready;

    /// <summary>Gets the exact device; loss and disposal reject access.</summary>
    public IGraphicDevice Device
    {
        get
        {
            GraphicsDeviceService.Entry? entry = Volatile.Read(ref _entry);
            ObjectDisposedException.ThrowIf(entry is null, this);
            IGraphicDevice device = entry.Device;
            if (device.Loss is { } loss)
            {
                throw new GraphicsDeviceLostException(loss);
            }

            ObjectDisposedException.ThrowIf(device.Status == GraphicsDeviceStatus.Disposed, device);
            return device;
        }
    }

    /// <summary>Returns the lease once without waiting for GPU work or destroying native objects.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _entry, null) is { } entry)
        {
            GraphicsDeviceService service = _service!;
            _service = null;
            service.Release(entry);
        }
    }
}
