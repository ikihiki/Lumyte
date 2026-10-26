using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics;

/// <summary>Owns one shared native resource on the physical device epoch that created it.</summary>
/// <typeparam name="T">The native resource contract.</typeparam>
public sealed class GraphicsResource<T>
    where T : class, IDisposable
{
    private readonly GraphicsResourceRuntime _runtime;
    private T? _value;

    internal GraphicsResource(T value, GraphicsResourceRuntime runtime, IGraphicDevice device, ulong epoch)
    {
        (_value, _runtime, Device, Epoch) = (value, runtime, device, epoch);
    }

    /// <summary>Gets the borrowed native value; retain and validate its ResourceLease before use.</summary>
    public T Value => _value ?? throw new ObjectDisposedException(nameof(GraphicsResource<T>));

    /// <summary>Gets the original physical device.</summary>
    public IGraphicDevice Device { get; }

    /// <summary>Gets the original physical device epoch.</summary>
    public ulong Epoch { get; }

    internal GraphicsResourceRuntime Runtime => _runtime;

    internal async ValueTask ReleaseAsync()
    {
        if (_value is null)
        {
            return;
        }

        if (Device.Status == GraphicsDeviceStatus.Lost)
        {
            await _runtime.WaitForRetirementAsync(Epoch);
        }

        _value.Dispose();
        _value = null;
    }
}
