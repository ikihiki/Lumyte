using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

/// <summary>Owns a Direct3D 12 device and direct queue.</summary>
public sealed class Direct3D12Device : IGraphicDevice, IDisposable
{
    private readonly GraphicsDeviceLifetime _lifetime;
    private readonly nint _native;

    private Direct3D12Device(nint native, bool partial)
    {
        _native = native;
        _lifetime = new(this);
        Queue = new Direct3D12Queue(this);
        SdkVersion = NativeMethods.DeviceSdkVersion(native);
        PartialGraphicsProgramsEnabled = partial;
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
        MaxStorageBufferBindingSize = 128 * 1024 * 1024,
        MaxTextureDimension2D = 16384,
        MaxTextureArrayLayers = 2048,
        MaxColorAttachments = 8,
        MaxSampledTexturesPerStage = 128,
        MaxSamplersPerStage = 16,
        MaxSamplerAnisotropy = 16,
        MaxUniformBuffersPerStage = 1,
        MaxStorageBuffersPerStage = 64,
        MaxComputeInvocationsPerWorkgroup = 1024,
        MaxComputeWorkgroupsPerDimension = 65535,
        MaxComputeWorkgroupSizeX = 1024,
        MaxComputeWorkgroupSizeY = 1024,
        MaxComputeWorkgroupSizeZ = 64,
        Features = GraphicsFeatures.IndirectDraw | GraphicsFeatures.AnisotropicFiltering | GraphicsFeatures.DepthBiasClamp,
    };

    /// <inheritdoc />
    public IGraphicsQueue Queue { get; }

    /// <summary>Gets the Agility SDK version queried from the created native device.</summary>
    public uint SdkVersion { get; }

    /// <summary>Gets a value indicating whether Preview partial programs were requested and validated on the selected driver.</summary>
    public bool PartialGraphicsProgramsEnabled { get; }

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

        int result = NativeMethods.DeviceCreate(desc.AdapterIndex, desc.UseWarp ? 1 : 0, desc.EnableDebugLayer ? 1 : 0, desc.EnablePartialGraphicsPrograms ? 1 : 0, sdkPath, out nint native);
        if (desc.EnablePartialGraphicsPrograms && result == unchecked((int)0x887A0004))
        {
            throw new NotSupportedException("Partial Graphics Programs require the Preview SDK, its state-object experiment, and a supporting driver.");
        }

        if (result == unchecked((int)0x887E0003))
        {
            throw new InvalidOperationException("The selected Agility SDK could not be loaded. Verify the runtime deployed in AgilitySDK; preview builds also require Windows Developer Mode.", Marshal.GetExceptionForHR(result));
        }

        Marshal.ThrowExceptionForHR(result);
        try
        {
            return new(native, desc.EnablePartialGraphicsPrograms);
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
        const BufferUsage Supported = BufferUsage.CopySource | BufferUsage.CopyDestination | BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.Index | BufferUsage.Indirect;
        if (size == 0 || size > Caps.MaxBufferSize || desc.Usage == 0 || !Enum.IsDefined(desc.Memory))
        {
            throw new ArgumentException("Invalid buffer size, usage or memory preference.", nameof(desc));
        }

        if ((desc.Usage & ~Supported) != 0 ||
            (desc.Memory == MemoryPreference.Upload && (desc.Usage & (BufferUsage.ShaderWrite | BufferUsage.CopyDestination)) != 0) ||
            (desc.Memory == MemoryPreference.Readback && desc.Usage != BufferUsage.CopyDestination))
        {
            throw new NotSupportedException("Buffer usage is incompatible with the selected heap.");
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
    public IGraphicsTexture CreateTexture(TextureDesc desc)
    {
        RequireReady();
        TextureValidation.Validate(desc, Caps);
        return new Direct3D12Texture(this, desc);
    }

    /// <inheritdoc />
    public IGraphicsSampler CreateSampler(SamplerDesc desc)
    {
        RequireReady();
        SamplerValidation.Validate(desc, Caps);
        return new Direct3D12Sampler(this, desc);
    }

    /// <inheritdoc />
    public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc)
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(desc);
        if ((ulong)desc.TextureCapacity + desc.SamplerCapacity + desc.BufferCapacity == 0)
        {
            throw new ArgumentException("An argument table must have at least one logical slot.", nameof(desc));
        }

        return new Direct3D12ArgumentTable(this, desc);
    }

    /// <inheritdoc />
    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(artifact);
        return new Direct3D12Shader(this, artifact);
    }

    /// <inheritdoc />
    public IGraphicsSemaphore CreateSemaphore()
    {
        RequireReady();
        return new Direct3D12Semaphore(this);
    }

    /// <inheritdoc />
    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format)
    {
        RequireReady();
        uint bytes = format switch
        {
            TextureFormat.R8Unorm => 1,
            TextureFormat.Rg8Unorm or TextureFormat.R16Float => 2,
            TextureFormat.Rgba16Float => 8,
            TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb or TextureFormat.Bgra8Unorm or TextureFormat.Bgra8Srgb or TextureFormat.Rg16Float or TextureFormat.Rgb10A2Unorm => 4,
            _ => throw new NotSupportedException("Only color texture copies are supported."),
        };
        return new() { BytesPerTexel = bytes, BytesPerRowAlignment = 256, BufferOffsetAlignmentInBytes = 512 };
    }

    /// <inheritdoc />
    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(desc);
        return new Direct3D12GraphicsPipeline(this, desc);
    }

    /// <inheritdoc />
    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(desc);
        return new Direct3D12ComputePipeline(this, desc);
    }

    /// <inheritdoc />
    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(artifact);
        return new Direct3D12ShaderDataBuffer<T>(this, artifact, count, memory);
    }

    /// <summary>Creates a surface borrowing a valid HWND; the caller owns the window and its message loop.</summary>
    /// <param name="window">An existing Win32 window handle.</param>
    /// <returns>An independently owned graphics surface.</returns>
    public IGraphicsSurface CreateSurface(nint window)
    {
        RequireReady();
        if (NativeMethods.WindowValid(window) == 0)
        {
            throw new ArgumentException("A valid HWND is required.", nameof(window));
        }

        return new Direct3D12Surface(this, window);
    }

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
}
