using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

/// <summary>Owns a browser WebGPU device and its immutable capability snapshot.</summary>
public sealed class BrowserDevice : IGraphicDevice, IDisposable
{
    private readonly JSObject _handle;
    private readonly object _resourceGate = new();
    private int _bufferCount;
    private int _textureCount;
    private bool _disposed;

    private BrowserDevice(JSObject handle, DeviceCaps caps)
    {
        (_handle, Caps) = (handle, caps);
    }

    /// <summary>Gets the effective GPUDevice limits captured during creation.</summary>
    public DeviceCaps Caps { get; }

    internal object ResourceGate => _resourceGate;

    internal JSObject Handle => _handle;

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

    /// <inheritdoc />
    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged
    {
        lock (_resourceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ulong size = (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            return new(size, size, Caps.CopyBufferOffsetAlignment, Caps.CopyBufferSizeAlignment);
        }
    }

    /// <inheritdoc />
    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(desc);
        lock (_resourceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            BufferLayout<T> layout = GetBufferLayout<T>();
            ulong size = layout.GetSizeInBytes(desc.Count);
            const BufferUsage KnownUsage = BufferUsage.CopySource | BufferUsage.CopyDestination | BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.Index;
            if (desc.Count == 0 || size > Caps.MaxBufferSize || desc.Usage == 0 || (desc.Usage & ~KnownUsage) != 0 || !Enum.IsDefined(desc.Memory))
            {
                throw new ArgumentException("Invalid buffer count, usage, memory preference or device limit.", nameof(desc));
            }

            if (desc.Memory != MemoryPreference.Automatic && size > int.MaxValue)
            {
                throw new NotSupportedException("CPU-mapped buffers must fit a managed byte span.");
            }

            var buffer = new BrowserBuffer<T>(this, desc, layout, size);
            _bufferCount++;
            return buffer;
        }
    }

    /// <inheritdoc />
    public IGraphicsTexture CreateTexture(TextureDesc desc)
    {
        lock (_resourceGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            TextureValidation.Validate(desc, Caps);
            var texture = new BrowserTexture(this, desc);
            _textureCount++;
            return texture;
        }
    }

    /// <summary>Destroys the WebGPU device and releases its JavaScript proxy; subsequent calls do nothing.</summary>
    public void Dispose()
    {
        lock (_resourceGate)
        {
            if (_bufferCount != 0 || _textureCount != 0)
            {
                throw new InvalidOperationException("Dispose all buffers and textures before disposing their device.");
            }

            if (_disposed)
            {
                return;
            }

            BrowserInterop.DestroyDevice(_handle);
            _handle.Dispose();
            _disposed = true;
        }
    }

    internal void ReleaseTexture()
    {
        lock (_resourceGate)
        {
            _textureCount--;
        }
    }

    internal void ReleaseBuffer()
    {
        lock (_resourceGate)
        {
            _bufferCount--;
        }
    }
}
