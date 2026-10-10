using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

/// <summary>Owns a native wgpu device and its immutable capability snapshot.</summary>
public sealed class WgpuDevice : IGraphicDevice, IDisposable
{
    private readonly A.Instance _instance;
    private readonly A.Adapter _adapter;
    private readonly A.Device _device;
    private bool _disposed;

    private WgpuDevice(A.Instance instance, A.Adapter adapter, A.Device device)
    {
        (_instance, _adapter, _device) = (instance, adapter, device);
        Queue = new WgpuQueue(this);
        WGPULimits limits = device.GetLimits();
        Limits = limits;
        Caps = new()
        {
            ShaderTarget = ShaderTarget.Wgsl,
            Features = GraphicsFeatures.IndirectDraw | GraphicsFeatures.AnisotropicFiltering,
            MaxBufferSize = limits.maxBufferSize,
            MaxStorageBufferBindingSize = limits.maxStorageBufferBindingSize,
            MaxTextureDimension2D = limits.maxTextureDimension2D,
            MaxTextureArrayLayers = limits.maxTextureArrayLayers,
            MaxColorAttachments = limits.maxColorAttachments,
            MaxSampledTexturesPerStage = limits.maxSampledTexturesPerShaderStage,
            MaxSamplersPerStage = limits.maxSamplersPerShaderStage,
            MaxSamplerAnisotropy = 16,
            MaxUniformBuffersPerStage = limits.maxUniformBuffersPerShaderStage,
            MaxStorageBuffersPerStage = limits.maxStorageBuffersPerShaderStage,
            MaxComputeWorkgroupsPerDimension = limits.maxComputeWorkgroupsPerDimension,
            MaxComputeWorkgroupSizeX = limits.maxComputeWorkgroupSizeX,
            MaxComputeWorkgroupSizeY = limits.maxComputeWorkgroupSizeY,
            MaxComputeWorkgroupSizeZ = limits.maxComputeWorkgroupSizeZ,
            MaxComputeInvocationsPerWorkgroup = limits.maxComputeInvocationsPerWorkgroup,
            CopyBufferOffsetAlignment = 4,
            CopyBufferSizeAlignment = 4,
            CopyBytesPerRowAlignment = 256,
            StorageBufferOffsetAlignment = limits.minStorageBufferOffsetAlignment,
        };
    }

    /// <inheritdoc />
    public IGraphicsQueue Queue { get; }

    /// <summary>Gets the effective device capabilities captured during creation.</summary>
    public DeviceCaps Caps { get; }

    internal WGPULimits Limits { get; }

    internal A.Device NativeDevice => _device;

    /// <summary>Creates a device independently of presentation targets using wgpu's default adapter selection and device limits.</summary>
    /// <returns>The owned native device; dispose it when the owner is finished.</returns>
    public static WgpuDevice Create()
    {
        var instance = A.Instance.Create();
        A.Adapter? adapter = null;
        A.Device? device = null;
        try
        {
            adapter = instance.RequestAdapterBlocking();
            device = adapter.RequestDeviceBlocking();
            return new(instance, adapter, device);
        }
        catch
        {
            device?.Dispose();
            adapter?.Dispose();
            instance.Dispose();
            throw;
        }
    }

    /// <summary>Connects distinct targets to this existing device without adapter reselection.</summary>
    /// <param name="sources">Nonempty borrowed target handles in result order.</param>
    /// <returns>The owned surfaces; failure releases only surfaces created by this call.</returns>
    public IReadOnlyList<IGraphicsSurface> CreateSurfaces(IReadOnlyList<A.SurfaceSource> sources)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(sources);
        A.SurfaceSource[] snapshot = sources.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("Supply at least one native presentation target.", nameof(sources));
        }

        var targets = new HashSet<(A.SurfaceSource.Kind, nint, nint)>();
        foreach (A.SurfaceSource source in snapshot)
        {
            ValidateSurfaceSource(source);
            if (!targets.Add((source.Tag, source.Handle0, source.Handle1)))
            {
                throw new ArgumentException("Supply distinct native presentation targets.", nameof(sources));
            }
        }

        var created = new List<IGraphicsSurface>();
        try
        {
            foreach (A.SurfaceSource source in snapshot)
            {
                created.Add(CreateSurface(source));
            }

            return created.AsReadOnly();
        }
        catch
        {
            foreach (IGraphicsSurface surface in created)
            {
                surface.Dispose();
            }

            throw;
        }
    }

    /// <summary>Connects another supplied native target to this device without adapter reselection.</summary>
    /// <param name="source">The borrowed native handles.</param>
    /// <returns>The owned surface if this adapter supports the target.</returns>
    public IGraphicsSurface CreateSurface(A.SurfaceSource source)
    {
        ValidateAlive();
        ValidateSurfaceSource(source);
        A.Surface native = _instance.CreateSurface(source);
        try
        {
            return new WgpuSurface(this, native, _adapter);
        }
        catch
        {
            native.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        var program = new WgpuGraphicsPipeline(this, desc);
        return program;
    }

    /// <inheritdoc />
    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        var program = new WgpuComputePipeline(this, desc);
        return program;
    }

    /// <inheritdoc />
    public IGraphicsSemaphore CreateSemaphore()
    {
        ValidateAlive();
        var semaphore = new WgpuSemaphore(this);
        return semaphore;
    }

    /// <inheritdoc />
    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        var commands = new WgpuCommandBuffer(this);
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

        var buffer = new WgpuBuffer<T>(this, desc, layout, size);
        return buffer;
    }

    /// <inheritdoc />
    public IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(artifact);
        var buffer = new WgpuShaderDataBuffer<T>(this, artifact, count, memory);
        return buffer;
    }

    /// <inheritdoc />
    public IGraphicsTexture CreateTexture(TextureDesc desc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TextureValidation.Validate(desc, Caps);
        var texture = new WgpuTexture(this, desc);
        return texture;
    }

    /// <inheritdoc />
    public IGraphicsSampler CreateSampler(SamplerDesc desc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SamplerValidation.Validate(desc, Caps);
        var sampler = new WgpuSampler(this, desc);
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

        var table = new WgpuArgumentTable(this, desc);
        return table;
    }

    /// <inheritdoc />
    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(artifact);
        var shader = new WgpuShader(this, artifact);
        return shader;
    }

    /// <summary>Releases the device, adapter and instance; subsequent calls do nothing.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _device.Dispose();
        _adapter.Dispose();
        _instance.Dispose();
        _disposed = true;
    }

    internal void ValidateAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static void ValidateSurfaceSource(A.SurfaceSource source)
    {
        if (!Enum.IsDefined(source.Tag) || source.Handle0 == 0 ||
            (source.Tag is A.SurfaceSource.Kind.WindowsHwnd or A.SurfaceSource.Kind.XlibWindow or A.SurfaceSource.Kind.WaylandSurface && source.Handle1 == 0))
        {
            throw new ArgumentException("Supply valid native target handles.", nameof(source));
        }
    }
}
