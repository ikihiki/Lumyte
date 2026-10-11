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
        Queue = new BrowserQueue(this);
    }

    /// <inheritdoc />
    public IGraphicsQueue Queue { get; }

    /// <summary>Gets the effective GPUDevice limits captured during creation.</summary>
    public DeviceCaps Caps { get; }

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

    /// <summary>Connects an already supplied GPUCanvasContext without querying the DOM or creating a canvas.</summary>
    /// <param name="context">The borrowed GPUCanvasContext of an HTMLCanvasElement or OffscreenCanvas.</param>
    /// <returns>The owned surface; the caller retains the context and canvas.</returns>
    public IGraphicsSurface CreateSurface(JSObject context)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(context);
        return new BrowserSurface(this, context);
    }

    /// <summary>Connects distinct externally supplied canvas contexts to this shared device.</summary>
    /// <param name="contexts">The nonempty borrowed contexts in result order.</param>
    /// <returns>The owned independent surfaces; failed creation releases every surface created by this call.</returns>
    public IReadOnlyList<IGraphicsSurface> CreateSurfaces(IReadOnlyList<JSObject> contexts)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(contexts);
        JSObject[] snapshot = contexts.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("Supply at least one GPUCanvasContext.", nameof(contexts));
        }

        var seen = new HashSet<JSObject>(ReferenceEqualityComparer.Instance);
        foreach (JSObject context in snapshot)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!seen.Add(context))
            {
                throw new ArgumentException("Supply distinct GPUCanvasContexts.", nameof(contexts));
            }
        }

        var surfaces = new List<IGraphicsSurface>();
        try
        {
            foreach (JSObject context in snapshot)
            {
                surfaces.Add(CreateSurface(context));
            }

            return surfaces.AsReadOnly();
        }
        catch
        {
            foreach (IGraphicsSurface surface in surfaces)
            {
                surface.Dispose();
            }

            throw;
        }
    }

    /// <inheritdoc />
    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        var program = new BrowserGraphicsPipeline(this, desc);
        return program;
    }

    /// <inheritdoc />
    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        var program = new BrowserComputePipeline(this, desc);
        return program;
    }

    /// <inheritdoc />
    public IGraphicsSemaphore CreateSemaphore()
    {
        ValidateAlive();
        var semaphore = new BrowserSemaphore(this);
        return semaphore;
    }

    /// <inheritdoc />
    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        var commands = new BrowserCommandBuffer(this);
        return commands;
    }

    /// <inheritdoc />
    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format)
    {
        ValidateAlive();
        if (!Enum.IsDefined(format) || format is TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8)
        {
            throw new NotSupportedException("Unknown color texture format.");
        }

        uint bytesPerTexel = format switch
        {
            TextureFormat.R8Unorm => 1,
            TextureFormat.Rg8Unorm => 2,
            TextureFormat.R16Float => 2,
            TextureFormat.Rg16Float => 4,
            TextureFormat.Rgba16Float => 8,
            TextureFormat.Rgb10A2Unorm => 4,
            _ => 4,
        };
        return new() { BytesPerTexel = bytesPerTexel, BufferOffsetAlignmentInBytes = Math.Max(4U, bytesPerTexel), BytesPerRowAlignment = Caps.CopyBytesPerRowAlignment };
    }

    /// <inheritdoc />
    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ulong size = (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        return new(size, size, Caps.CopyBufferOffsetAlignment, Caps.CopyBufferSizeAlignment);
    }

    /// <inheritdoc />
    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(desc);
        ObjectDisposedException.ThrowIf(_disposed, this);
        BufferLayout<T> layout = GetBufferLayout<T>();
        ulong size = layout.GetSizeInBytes(desc.Count);
        const BufferUsage KnownUsage = BufferUsage.CopySource | BufferUsage.CopyDestination | BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.Index | BufferUsage.Indirect;
        if (desc.Count == 0 || size > Caps.MaxBufferSize || desc.Usage == 0 || (desc.Usage & ~KnownUsage) != 0 || !Enum.IsDefined(desc.Memory))
        {
            throw new ArgumentException("Invalid buffer count, usage, memory preference or device limit.", nameof(desc));
        }

        if (desc.Memory != MemoryPreference.Automatic && size > int.MaxValue)
        {
            throw new NotSupportedException("CPU-mapped buffers must fit a managed byte span.");
        }

        var buffer = new BrowserBuffer<T>(this, desc, layout, size);
        return buffer;
    }

    /// <inheritdoc />
    public IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(artifact);
        var buffer = new BrowserShaderDataBuffer<T>(this, artifact, count, memory);
        return buffer;
    }

    /// <inheritdoc />
    public IGraphicsTexture CreateTexture(TextureDesc desc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TextureValidation.Validate(desc, Caps);
        var texture = new BrowserTexture(this, desc);
        return texture;
    }

    /// <inheritdoc />
    public IGraphicsSampler CreateSampler(SamplerDesc desc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SamplerValidation.Validate(desc, Caps);
        var sampler = new BrowserSampler(this, desc);
        return sampler;
    }

    /// <inheritdoc />
    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(desc);
        if ((ulong)desc.TextureCapacity + desc.SamplerCapacity + desc.BufferCapacity == 0)
        {
            throw new ArgumentException("An argument table must have at least one logical slot.", nameof(desc));
        }

        var table = new BrowserArgumentTable(this, desc);
        return table;
    }

    /// <inheritdoc />
    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(artifact);
        var shader = new BrowserShader(this, artifact);
        return shader;
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

    internal void ValidateAlive() => ObjectDisposedException.ThrowIf(_disposed, this);
}
