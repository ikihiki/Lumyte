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
        WGPULimits limits = device.GetLimits();
        Caps = new()
        {
            Features = GraphicsFeatures.IndirectDraw | GraphicsFeatures.AnisotropicFiltering,
            MaxBufferSize = limits.maxBufferSize,
            MaxStorageBufferBindingSize = limits.maxStorageBufferBindingSize,
            MaxTextureDimension2D = limits.maxTextureDimension2D,
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
}
