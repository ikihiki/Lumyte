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
    private int _surfaceCount;
    private int _bufferCount;
    private int _textureCount;
    private int _samplerCount;
    private int _argumentTableCount;
    private int _shaderCount;
    private int _pipelineCount;
    private int _commandCount;
    private int _submissionCount;
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

    /// <summary>Creates a headless device using wgpu's default adapter selection and device limits.</summary>
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

    /// <summary>Creates a device compatible with the supplied native target; no window is created or inspected.</summary>
    /// <param name="source">The borrowed native window or layer handles supplied by the caller.</param>
    /// <param name="surface">The owned graphics surface; dispose it before the returned device.</param>
    /// <returns>The device selected for the supplied target.</returns>
    public static WgpuDevice CreateForPresentation(A.SurfaceSource source, out IGraphicsSurface surface)
    {
        ValidateSurfaceSource(source);
        var instance = A.Instance.Create();
        A.Surface native = default;
        A.Adapter? adapter = null;
        A.Device? device = null;
        try
        {
            native = instance.CreateSurface(source);
            adapter = instance.RequestAdapterBlocking(native);
            device = adapter.RequestDeviceBlocking();
            var owner = new WgpuDevice(instance, adapter, device);
            surface = new WgpuSurface(owner, native, adapter);
            return owner;
        }
        catch
        {
            device?.Dispose();
            adapter?.Dispose();
            native.Dispose();
            instance.Dispose();
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
        _pipelineCount++;
        return program;
    }

    /// <inheritdoc />
    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        var program = new WgpuComputePipeline(this, desc);
        _pipelineCount++;
        return program;
    }

    /// <inheritdoc />
    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        var commands = new WgpuCommandBuffer(this);
        _commandCount++;
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
        _bufferCount++;
        return buffer;
    }

    /// <inheritdoc />
    public IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(artifact);
        var buffer = new WgpuShaderDataBuffer<T>(this, artifact, count, memory);
        _bufferCount++;
        return buffer;
    }

    /// <inheritdoc />
    public IGraphicsTexture CreateTexture(TextureDesc desc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TextureValidation.Validate(desc, Caps);
        var texture = new WgpuTexture(this, desc);
        _textureCount++;
        return texture;
    }

    /// <inheritdoc />
    public IGraphicsSampler CreateSampler(SamplerDesc desc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SamplerValidation.Validate(desc, Caps);
        var sampler = new WgpuSampler(this, desc);
        _samplerCount++;
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
        _argumentTableCount++;
        return table;
    }

    /// <inheritdoc />
    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(artifact);
        var shader = new WgpuShader(this, artifact);
        _shaderCount++;
        return shader;
    }

    /// <summary>Releases the device, adapter and instance; subsequent calls do nothing.</summary>
    public void Dispose()
    {
        if (_surfaceCount != 0 || _bufferCount != 0 || _textureCount != 0 || _samplerCount != 0 || _argumentTableCount != 0 || _pipelineCount != 0 || _shaderCount != 0 || _commandCount != 0 || _submissionCount != 0)
        {
            throw new InvalidOperationException("Dispose all argument tables, buffers, textures, samplers, shaders, pipelines, commands and submissions before disposing their device.");
        }

        if (_disposed)
        {
            return;
        }

        _device.Dispose();
        _adapter.Dispose();
        _instance.Dispose();
        _disposed = true;
    }

    internal void ReleasePipeline() => _pipelineCount--;

    internal void ReleaseCommand() => _commandCount--;

    internal void RetainSubmission() => _submissionCount++;

    internal void ReleaseSubmission() => _submissionCount--;

    internal void ValidateAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal void ReleaseShader() => _shaderCount--;

    internal void ReleaseArgumentTable() => _argumentTableCount--;

    internal void ReleaseSampler() => _samplerCount--;

    internal void RetainSurface() => _surfaceCount++;

    internal void ReleaseSurface() => _surfaceCount--;

    internal void ReleaseTexture() => _textureCount--;

    internal void ReleaseBuffer() => _bufferCount--;

    private static void ValidateSurfaceSource(A.SurfaceSource source)
    {
        if (!Enum.IsDefined(source.Tag) || source.Handle0 == 0 ||
            (source.Tag is A.SurfaceSource.Kind.WindowsHwnd or A.SurfaceSource.Kind.XlibWindow or A.SurfaceSource.Kind.WaylandSurface && source.Handle1 == 0))
        {
            throw new ArgumentException("Supply valid native target handles.", nameof(source));
        }
    }
}
