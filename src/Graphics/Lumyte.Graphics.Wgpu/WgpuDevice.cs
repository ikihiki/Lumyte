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
    private int _bufferCount;
    private int _textureCount;
    private int _samplerCount;
    private int _argumentTableCount;
    private int _shaderCount;
    private bool _disposed;

    private WgpuDevice(A.Instance instance, A.Adapter adapter, A.Device device)
    {
        (_instance, _adapter, _device) = (instance, adapter, device);
        WGPULimits limits = device.GetLimits();
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
            MaxComputeInvocationsPerWorkgroup = limits.maxComputeInvocationsPerWorkgroup,
            CopyBufferOffsetAlignment = 4,
            CopyBufferSizeAlignment = 4,
            CopyBytesPerRowAlignment = 256,
            StorageBufferOffsetAlignment = limits.minStorageBufferOffsetAlignment,
        };
    }

    /// <summary>Gets the effective device capabilities captured during creation.</summary>
    public DeviceCaps Caps { get; }

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
        const BufferUsage KnownUsage = BufferUsage.CopySource | BufferUsage.CopyDestination | BufferUsage.ShaderRead | BufferUsage.ShaderWrite | BufferUsage.Index;
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
        if (_bufferCount != 0 || _textureCount != 0 || _samplerCount != 0 || _argumentTableCount != 0 || _shaderCount != 0)
        {
            throw new InvalidOperationException("Dispose all argument tables, buffers, textures, samplers and shaders before disposing their device.");
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

    internal void ReleaseShader() => _shaderCount--;

    internal void ReleaseArgumentTable() => _argumentTableCount--;

    internal void ReleaseSampler() => _samplerCount--;

    internal void ReleaseTexture() => _textureCount--;

    internal void ReleaseBuffer() => _bufferCount--;
}
