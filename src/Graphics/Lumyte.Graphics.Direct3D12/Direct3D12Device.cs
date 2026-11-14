using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

/// <summary>Owns a Direct3D 12 device and direct queue.</summary>
public sealed class Direct3D12Device : IGraphicDevice, IDisposable
{
    /// <summary>The application root capacity in DWORDs after the six-DWORD fixed root-signature cost.</summary>
    public const int RootConstantDwordCapacity = 58;

    /// <summary>The maximum logical application root wire size in bytes.</summary>
    public const int RootConstantByteCapacity = RootConstantDwordCapacity * sizeof(uint);

    private readonly GraphicsDeviceLifetime _lifetime;

    private readonly nint _native;
    private readonly IDirect3D12QueueDriver? _queueDriver;
    private bool _hasCreatedResources;
    private Direct3D12Submission? _failedSubmissions;

    internal Direct3D12Device(IDirect3D12QueueDriver driver, bool validation)
    {
        _queueDriver = driver;
        _lifetime = new(this);
        Queue = new Direct3D12Queue(this);
        if (validation)
        {
            EnableValidation();
        }
    }

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

    /// <summary>Gets optional managed diagnostics, null by default in all build configurations.</summary>
    public GraphicsValidation? Validation { get; private set; }

    /// <inheritdoc />
    public bool ValidationEnabled => Validation != null;

    /// <inheritdoc />
    public GraphicsDeviceStatus Status => _lifetime.Status;

    /// <inheritdoc />
    public GraphicsDeviceLoss? Loss => _lifetime.Loss;

    /// <inheritdoc />
    public DeviceCaps Caps { get; } = new()
    {
        ShaderTarget = ShaderTarget.Dxil,
        ArgumentTableBindingLimits = new()
        {
            TextureSlots = 128,
            SamplerSlots = 16,
            BufferSlots = 64,
            DirectSlotBindings = true,
            TexturesPerDraw = 128,
            SamplersPerDraw = 16,
            BuffersPerDraw = 64,
        },
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

    internal nint QueueNative
    {
        get
        {
            // Removal probes remain valid while lost, but never use a freed owner.
            ObjectDisposedException.ThrowIf(Status == GraphicsDeviceStatus.Disposed, this);
            return _native;
        }
    }

    internal IDirect3D12QueueDriver QueueDriver => _queueDriver ?? NativeDirect3D12QueueDriver.Instance;

    internal Direct3D12Submission? FailedSubmission => _failedSubmissions;

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

        var child = new Direct3D12Buffer<T>(this, desc, layout, size);
        _hasCreatedResources = true;
        Validation?.Register(child, this);
        return child;
    }

    /// <summary>Enables managed diagnostics before creating children. Keep this device's operations externally serialized.</summary>
    public void EnableValidation()
    {
        if (Validation == null && _hasCreatedResources)
        {
            throw new InvalidOperationException("Enable validation before creating device resources.");
        }

        Validation ??= new GraphicsValidation();
    }

    /// <inheritdoc />
    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        RequireReady();
        var child = new Direct3D12CommandBuffer(this);
        _hasCreatedResources = true;
        Validation?.Register(child, this);
        return child;
    }

    /// <inheritdoc />
    public async ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Status == GraphicsDeviceStatus.Disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        int removed = QueueDriver.DeviceRemoved(this);
        if (removed >= 0)
        {
            if (_failedSubmissions == null)
            {
                throw new InvalidOperationException("Retirement requires confirmed native device removal or a partially issued submission.");
            }

            try
            {
                while (_failedSubmissions is { } pending)
                {
                    if (!pending.HasNativeUse)
                    {
                        pending.AbandonUnissued();
                    }
                    else
                    {
                        pending.EnsureCompletionSignal();
                        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
                        pending.Dispose();
                    }

                    _failedSubmissions = pending.NextFailed;
                    pending.NextFailed = null;
                }

                return;
            }
            catch (Exception error) when (error is not OperationCanceledException && QueueDriver.DeviceRemoved(this) < 0)
            {
                // Native removal during recovery is also an explicit retirement
                // confirmation, not a reason to lose the retained owners.
                removed = QueueDriver.DeviceRemoved(this);
            }
        }

        _lifetime.ReportLoss($"0x{removed:X8}", "Direct3D 12 device was removed.");
        while (_failedSubmissions is { } pending)
        {
            pending.RetireAfterDeviceRemoval();
            _failedSubmissions = pending.NextFailed;
            pending.NextFailed = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        RetireCompletedFailedSubmissions();
        if (_failedSubmissions != null)
        {
            throw new InvalidOperationException("Retire partial native submissions before device disposal.");
        }

        Validation?.Release(this);
        if (Status == GraphicsDeviceStatus.Disposed)
        {
            return;
        }

        if (_native != 0)
        {
            NativeMethods.DeviceDestroy(_native);
        }

        _lifetime.Dispose();
    }

    /// <inheritdoc />
    public IGraphicsTexture CreateTexture(TextureDesc desc)
    {
        RequireReady();
        TextureValidation.Validate(desc, Caps);
        var child = new Direct3D12Texture(this, desc);
        Validation?.SetTextureState(child, TextureState.Undefined);
        _hasCreatedResources = true;
        Validation?.Register(child, this);
        return child;
    }

    /// <inheritdoc />
    public IGraphicsSampler CreateSampler(SamplerDesc desc)
    {
        RequireReady();
        SamplerValidation.Validate(desc, Caps);
        var child = new Direct3D12Sampler(this, desc);
        _hasCreatedResources = true;
        Validation?.Register(child, this);
        return child;
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

        var child = new Direct3D12ArgumentTable(this, desc);
        _hasCreatedResources = true;
        Validation?.Register(child, this);
        return child;
    }

    /// <inheritdoc />
    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(artifact);
        var child = new Direct3D12Shader(this, artifact);
        _hasCreatedResources = true;
        Validation?.Register(child, this);
        return child;
    }

    /// <inheritdoc />
    public IGraphicsSemaphore CreateSemaphore()
    {
        RequireReady();
        var child = new Direct3D12Semaphore(this);
        _hasCreatedResources = true;
        Validation?.Register(child, this);
        return child;
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
        var child = new Direct3D12GraphicsPipeline(this, desc);
        _hasCreatedResources = true;
        Validation?.Register(child, this);
        return child;
    }

    /// <inheritdoc />
    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        RequireReady();
        ArgumentNullException.ThrowIfNull(desc);
        var child = new Direct3D12ComputePipeline(this, desc);
        _hasCreatedResources = true;
        Validation?.Register(child, this);
        return child;
    }

    /// <summary>Creates raw unmanaged shader storage with Debug-only layout diagnostics.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="artifact">The compiled shader artifact.</param>
    /// <param name="count">The element count.</param>
    /// <param name="memory">The memory preference.</param>
    /// <param name="usage">The GPU usages; upload staging uses CopySource.</param>
    /// <returns>The typed raw buffer, without conversion or dependency metadata.</returns>
    public IGraphicsBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic, BufferUsage usage = BufferUsage.ShaderRead | BufferUsage.CopyDestination)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (Validation != null)
        {
            ShaderDataLayout.ValidateUnmanaged<T>(artifact, Caps.ShaderTarget);
        }

        return CreateBuffer<T>(new() { Count = count, Memory = memory, Usage = memory == MemoryPreference.Upload ? BufferUsage.CopySource : usage });
    }

    /// <inheritdoc />
    public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : IShaderArguments
    {
        RequireReady();

        ArgumentNullException.ThrowIfNull(artifact);
        var child = new Direct3D12ShaderDataBuffer<T>(this, artifact, count, memory);
        _hasCreatedResources = true;
        Validation?.Register(child, this);
        return child;
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

        var child = new Direct3D12Surface(this, window);
        _hasCreatedResources = true;
        return child;
    }

    internal void RetainFailedSubmission(Direct3D12Submission submission)
    {
        submission.NextFailed = _failedSubmissions;
        _failedSubmissions = submission;
    }

    internal void RetireCompletedFailedSubmissions()
    {
        Direct3D12Submission? previous = null;
        Direct3D12Submission? pending = _failedSubmissions;
        while (pending != null)
        {
            Direct3D12Submission? next = pending.NextFailed;
            if (!pending.HasNativeUse || pending.Status is SubmissionStatus.Completed or SubmissionStatus.Failed)
            {
                if (!pending.HasNativeUse)
                {
                    pending.AbandonUnissued();
                }
                else
                {
                    pending.Dispose();
                }

                if (previous == null)
                {
                    _failedSubmissions = next;
                }
                else
                {
                    previous.NextFailed = next;
                }

                pending.NextFailed = null;
            }
            else
            {
                previous = pending;
            }

            pending = next;
        }
    }

    internal void RequireReady()
    {
        RetireCompletedFailedSubmissions();

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

        int removed = QueueDriver.DeviceRemoved(this);
        if (removed < 0)
        {
            _lifetime.ReportLoss($"0x{removed:X8}", "Direct3D 12 device was removed.");
        }

        Marshal.ThrowExceptionForHR(result);
    }
}
