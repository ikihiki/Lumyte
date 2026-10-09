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
    private readonly object _resourceGate = new();
    private int _bufferCount;
    private int _textureCount;
    private bool _disposed;

    private WgpuDevice(A.Instance instance, A.Adapter adapter, A.Device device)
    {
        (_instance, _adapter, _device) = (instance, adapter, device);
        WGPULimits limits = device.GetLimits();
        Caps = new()
        {
            Features = GraphicsFeatures.IndirectDraw | GraphicsFeatures.AnisotropicFiltering,
            MaxBufferSize = limits.maxBufferSize,
            MaxStorageBufferBindingSize = limits.maxStorageBufferBindingSize,
            MaxTextureDimension2D = limits.maxTextureDimension2D,
            MaxTextureArrayLayers = limits.maxTextureArrayLayers,
            MaxColorAttachments = limits.maxColorAttachments,
            MaxSampledTexturesPerStage = limits.maxSampledTexturesPerShaderStage,
            MaxSamplersPerStage = limits.maxSamplersPerShaderStage,
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

    internal object ResourceGate => _resourceGate;

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

            var buffer = new WgpuBuffer<T>(this, desc, layout, size);
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
            var texture = new WgpuTexture(this, desc);
            _textureCount++;
            return texture;
        }
    }

    /// <summary>Releases the device, adapter and instance; subsequent calls do nothing.</summary>
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

            _device.Dispose();
            _adapter.Dispose();
            _instance.Dispose();
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
