using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Browser;
using Lumyte.Graphics.Hosting;

namespace Lumyte.UI.Application.Browser;

internal sealed class BrowserDeviceFactory : IGraphicsDeviceFactory
{
    private readonly IUiDispatcher _dispatcher;

    public BrowserDeviceFactory(IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    public async ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default)
    {
        if (!_dispatcher.CheckAccess())
        {
            Task<BrowserDevice> pending = await _dispatcher.InvokeAsync(() => BrowserDevice.CreateAsync("./lumyte-graphics.js", cancellationToken), cancellationToken);
            return new Owner(await pending, _dispatcher);
        }

        return new Owner(await BrowserDevice.CreateAsync("./lumyte-graphics.js", cancellationToken), _dispatcher);
    }

    private sealed class Owner(BrowserDevice device, IUiDispatcher dispatcher) : IGraphicsDeviceOwner
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
                device.Dispose();
            }
            else
            {
                await dispatcher.InvokeAsync(device.Dispose);
            }

            _disposed = true;
        }
    }
}
