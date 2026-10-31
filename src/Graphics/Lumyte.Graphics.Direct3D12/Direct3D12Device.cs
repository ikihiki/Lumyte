using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Direct3D12;

/// <summary>Owns a Direct3D 12 device and direct queue for explicit buffer transfers.</summary>
public sealed class Direct3D12Device : IGraphicDevice, IDisposable
{
    private readonly GraphicsDeviceLifetime _lifetime;
    private readonly nint _native;

    private Direct3D12Device(nint native)
    {
        _native = native;
        _lifetime = new(this);
        Queue = new Direct3D12Queue(this);
        SdkVersion = NativeMethods.DeviceSdkVersion(native);
    }

    /// <inheritdoc />
    public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
    {
        add => _lifetime.DeviceLost += value;
        remove => _lifetime.DeviceLost -= value;
    }

    /// <inheritdoc />
    public GraphicsDeviceStatus Status => _lifetime.Status;

    /// <inheritdoc />
    public GraphicsDeviceLoss? Loss => _lifetime.Loss;

    /// <inheritdoc />
    public DeviceCaps Caps { get; } = new()
    {
        ShaderTarget = ShaderTarget.Dxil,
        MaxBufferSize = 128 * 1024 * 1024,
        CopyBytesPerRowAlignment = 256,
    };

    /// <inheritdoc />
    public IGraphicsQueue Queue { get; }

    /// <summary>Gets the Agility SDK version queried from the created native device.</summary>
    public uint SdkVersion { get; }

    internal nint Native
    {
        get
        {
            RequireReady();
            return _native;
        }
    }

    /// <summary>Creates an explicitly selected hardware or WARP device without fallback.</summary>
    /// <param name="desc">Adapter selection and optional debug validation.</param>
    /// <returns>The owned native device.</returns>
    public static Direct3D12Device Create(Direct3D12DeviceDesc? desc = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Direct3D 12 requires Windows.");
        }

        desc ??= new();
        if (desc.UseWarp && desc.AdapterIndex != 0)
        {
            throw new ArgumentException("WARP selection cannot specify a hardware adapter index.", nameof(desc));
        }

        string sdkPath = Path.Combine(AppContext.BaseDirectory, "AgilitySDK") + Path.DirectorySeparatorChar;
        if (!File.Exists(Path.Combine(sdkPath, "D3D12Core.dll")))
        {
            throw new FileNotFoundException("The pinned Agility SDK runtime is missing from the application output.", Path.Combine(sdkPath, "D3D12Core.dll"));
        }

        int result = NativeMethods.DeviceCreate(desc.AdapterIndex, desc.UseWarp ? 1 : 0, desc.EnableDebugLayer ? 1 : 0, sdkPath, out nint native);
        if (result == unchecked((int)0x887E0003))
        {
            throw new InvalidOperationException("The selected Agility SDK could not be loaded. Verify the runtime deployed in AgilitySDK; preview builds also require Windows Developer Mode.", Marshal.GetExceptionForHR(result));
        }

        Marshal.ThrowExceptionForHR(result);
        try
        {
            return new(native);
        }
        catch
        {
            NativeMethods.DeviceDestroy(native);
            throw;
        }
    }

    /// <inheritdoc />
    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged
    {
        RequireReady();
        ulong size = (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        return new(size, size, 1, 1);
    }

    /// <inheritdoc />
    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(desc);
        BufferLayout<T> layout = GetBufferLayout<T>();
        ulong size = layout.GetSizeInBytes(desc.Count);
        const BufferUsage Supported = BufferUsage.CopySource | BufferUsage.CopyDestination;
        if (size == 0 || size > Caps.MaxBufferSize || desc.Usage == 0 || !Enum.IsDefined(desc.Memory))
        {
            throw new ArgumentException("Invalid buffer size, usage or memory preference.", nameof(desc));
        }

        if ((desc.Usage & ~Supported) != 0 ||
            (desc.Memory == MemoryPreference.Upload && desc.Usage != BufferUsage.CopySource) ||
            (desc.Memory == MemoryPreference.Readback && desc.Usage != BufferUsage.CopyDestination))
        {
            throw new NotSupportedException("This backend supports transfer buffers; Upload is copy-source only and Readback is copy-destination only.");
        }

        return new Direct3D12Buffer<T>(this, desc, layout, size);
    }

    /// <inheritdoc />
    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        RequireReady();
        return new Direct3D12CommandBuffer(this);
    }

    /// <inheritdoc />
    public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Status == GraphicsDeviceStatus.Disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        int removed = NativeMethods.DeviceRemoved(_native);
        if (removed >= 0)
        {
            throw new InvalidOperationException("Retirement requires confirmed native device removal.");
        }

        _lifetime.ReportLoss($"0x{removed:X8}", "Direct3D 12 device was removed.");
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Status == GraphicsDeviceStatus.Disposed)
        {
            return;
        }

        NativeMethods.DeviceDestroy(_native);
        _lifetime.Dispose();
    }

    /// <inheritdoc />
    public IGraphicsTexture CreateTexture(TextureDesc desc) => throw Unsupported();

    /// <inheritdoc />
    public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw Unsupported();

    /// <inheritdoc />
    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw Unsupported();

    /// <inheritdoc />
    public IGraphicsShader CreateShader(ShaderArtifact artifact) => throw Unsupported();

    /// <inheritdoc />
    public IGraphicsSemaphore CreateSemaphore() => throw Unsupported();

    /// <inheritdoc />
    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw Unsupported();

    /// <inheritdoc />
    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw Unsupported();

    /// <inheritdoc />
    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw Unsupported();

    /// <inheritdoc />
    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData => throw Unsupported();

    internal void RequireReady()
    {
        ObjectDisposedException.ThrowIf(Status == GraphicsDeviceStatus.Disposed, this);
        if (Status == GraphicsDeviceStatus.Lost)
        {
            throw new InvalidOperationException("Direct3D 12 device is lost.");
        }
    }

    internal void Check(int result)
    {
        if (result >= 0)
        {
            return;
        }

        int removed = NativeMethods.DeviceRemoved(_native);
        if (removed < 0)
        {
            _lifetime.ReportLoss($"0x{removed:X8}", "Direct3D 12 device was removed.");
        }

        Marshal.ThrowExceptionForHR(result);
    }

    private NotSupportedException Unsupported()
    {
        RequireReady();
        return new("The Direct3D 12 backend currently supports device creation and buffer transfers only.");
    }
}
