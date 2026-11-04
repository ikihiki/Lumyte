using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Wgpu;

namespace Lumyte.UI.Application.Desktop;

internal sealed class DesktopDeviceFactory : IGraphicsDeviceFactory
{
    private readonly IUiDispatcher _dispatcher;

    public DesktopDeviceFactory(IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    public async ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default)
    {
        IGraphicDevice device = _dispatcher.CheckAccess() ? WgpuDevice.Create() : await _dispatcher.InvokeAsync<IGraphicDevice>(() => WgpuDevice.Create(), cancellationToken);
        return new Owner(device, _dispatcher);
    }

    private sealed class Owner(IGraphicDevice device, IUiDispatcher dispatcher) : IGraphicsDeviceOwner
    {
        private bool _disposed;

        public IGraphicDevice Device => device;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                ((IDisposable)device).Dispose();
            }
            else
            {
                await dispatcher.InvokeAsync(() => ((IDisposable)device).Dispose());
            }

            _disposed = true;
        }
    }
}
