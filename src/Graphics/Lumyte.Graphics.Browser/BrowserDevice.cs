using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

/// <summary>Owns a browser WebGPU device and its immutable capability snapshot.</summary>
public sealed class BrowserDevice : IGraphicDevice, IDisposable
{
    private readonly JSObject _handle;
    private bool _disposed;

    private BrowserDevice(JSObject handle, DeviceCaps caps)
    {
        (_handle, Caps) = (handle, caps);
    }

    /// <summary>Gets the effective GPUDevice limits captured during creation.</summary>
    public DeviceCaps Caps { get; }

    /// <summary>Imports the browser module and requests a WebGPU device from the default adapter.</summary>
    /// <param name="moduleUrl">The URL serving this package's lumyte-graphics.js module.</param>
    /// <param name="cancellationToken">Cancels import or prevents retaining a device created after cancellation.</param>
    /// <returns>The owned browser device.</returns>
    public static async Task<BrowserDevice> CreateAsync(string moduleUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleUrl);
        cancellationToken.ThrowIfCancellationRequested();
        using JSObject location = JSHost.GlobalThis.GetPropertyAsJSObject("location") ?? throw new InvalidOperationException("The browser has no location for resolving the module URL.");
        string baseUrl = location.GetPropertyAsString("href") ?? throw new InvalidOperationException("The browser location has no URL.");
        var resolvedUrl = new Uri(new Uri(baseUrl), moduleUrl);
        await JSHost.ImportAsync("Lumyte.Graphics.Browser", resolvedUrl.AbsoluteUri, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        JSObject handle = await BrowserInterop.CreateDeviceAsync();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeviceCaps caps = JsonSerializer.Deserialize(BrowserInterop.GetCapsJson(handle), BrowserJsonContext.Default.DeviceCaps) ?? throw new InvalidOperationException("WebGPU returned no capability data.");
            return new(handle, caps);
        }
        catch
        {
            BrowserInterop.DestroyDevice(handle);
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Destroys the WebGPU device and releases its JavaScript proxy; subsequent calls do nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        BrowserInterop.DestroyDevice(_handle);
        _handle.Dispose();
        _disposed = true;
    }
}
