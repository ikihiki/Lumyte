using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Hosting;

/// <summary>Retains one exact device epoch without transferring native ownership.</summary>
public sealed class GraphicsDeviceLease : IDisposable
{
    private readonly IGraphicDevice _device;
    private Action? _release;

    internal GraphicsDeviceLease(IGraphicDevice device, GraphicsDeviceState state, Action release)
    {
        (_device, State, _release) = (device, state, release);
    }

    /// <summary>Gets the Ready state captured when this lease was acquired.</summary>
    public GraphicsDeviceState State { get; }

    /// <summary>Gets a value indicating whether the lease is retained and its physical device remains Ready.</summary>
    public bool IsUsable => Volatile.Read(ref _release) is not null && _device.Status == GraphicsDeviceStatus.Ready;

    /// <summary>Gets the exact device; loss and disposal reject access.</summary>
    public IGraphicDevice Device
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _release) is null, this);
            if (_device.Loss is { } loss)
            {
                throw new GraphicsDeviceLostException(loss);
            }

            ObjectDisposedException.ThrowIf(_device.Status == GraphicsDeviceStatus.Disposed, _device);
            return _device;
        }
    }

    /// <summary>Returns the lease once without waiting for GPU work or destroying native objects.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
