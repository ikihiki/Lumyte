using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

/// <summary>Owns a Vulkan 1.3 instance and logical device with cached capabilities.</summary>
public sealed unsafe class VulkanDevice : IGraphicDevice, IDisposable
{
    private readonly Vk _api;
    private readonly Instance _instance;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Queue _nativeQueue;
    private int _bufferCount;
    private int _textureCount;
    private int _samplerCount;
    private int _argumentTableCount;
    private int _shaderCount;
    private int _pipelineCount;
    private int _commandCount;
    private int _submissionCount;
    private bool _disposed;

    private VulkanDevice(Vk api, Instance instance, Device device, PhysicalDevice physicalDevice, DeviceCaps caps, bool supportsCubeArrays, uint queueFamily)
    {
        (_api, _instance, _device, Caps) = (api, instance, device, caps);
        _physicalDevice = physicalDevice;
        SupportsCubeArrays = supportsCubeArrays;
        QueueFamily = queueFamily;
        api.GetDeviceQueue(device, queueFamily, 0, out _nativeQueue);
        Queue = new VulkanQueue(this);
    }

    /// <inheritdoc />
    public IGraphicsQueue Queue { get; }

    /// <summary>Gets the enabled capabilities and physical device limits captured during creation.</summary>
    public DeviceCaps Caps { get; }

    internal uint QueueFamily { get; }

    internal Queue NativeQueue => _nativeQueue;

    internal bool SupportsCubeArrays { get; }

    internal Vk Api => _api;

    internal Device NativeDevice => _device;

    internal PhysicalDevice PhysicalDevice => _physicalDevice;

    /// <summary>Creates a Vulkan 1.3 device with a general queue, maintenance4, dynamic rendering and synchronization2.</summary>
    /// <param name="physicalDeviceIndex">The zero-based device index in the Vulkan enumeration.</param>
    /// <returns>The owned instance and logical device.</returns>
    public static VulkanDevice Create(uint physicalDeviceIndex = 0)
    {
        var api = Vk.GetApi();
        Instance instance = default;
        Device device = default;
        try
        {
            var application = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version13 };
            var instanceInfo = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &application };
            Check(api.CreateInstance(&instanceInfo, null, &instance), "CreateInstance");
            PhysicalDevice physical = SelectPhysicalDevice(api, instance, physicalDeviceIndex);
            var properties13 = new PhysicalDeviceVulkan13Properties { SType = StructureType.PhysicalDeviceVulkan13Properties };
            var properties = new PhysicalDeviceProperties2 { SType = StructureType.PhysicalDeviceProperties2, PNext = &properties13 };
            api.GetPhysicalDeviceProperties2(physical, &properties);
            if (properties.Properties.ApiVersion < Vk.Version13)
            {
                throw new NotSupportedException("The selected adapter must support Vulkan 1.3.");
            }

            var supported13 = new PhysicalDeviceVulkan13Features { SType = StructureType.PhysicalDeviceVulkan13Features };
            var supported = new PhysicalDeviceFeatures2 { SType = StructureType.PhysicalDeviceFeatures2, PNext = &supported13 };
            api.GetPhysicalDeviceFeatures2(physical, &supported);
            if (!supported13.Maintenance4 || !supported13.DynamicRendering || !supported13.Synchronization2 || !supported.Features.IndependentBlend)
            {
                throw new NotSupportedException("Vulkan maintenance4, dynamic rendering, synchronization2 and independent blending are required.");
            }

            uint queueFamily = SelectQueueFamily(api, physical);
            float priority = 1;
            var queueInfo = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = queueFamily, QueueCount = 1, PQueuePriorities = &priority };
            var enabled = new PhysicalDeviceFeatures { SamplerAnisotropy = supported.Features.SamplerAnisotropy, DepthBiasClamp = supported.Features.DepthBiasClamp, ImageCubeArray = supported.Features.ImageCubeArray, IndependentBlend = true };
            var enabled13 = new PhysicalDeviceVulkan13Features { SType = StructureType.PhysicalDeviceVulkan13Features, Maintenance4 = true, DynamicRendering = true, Synchronization2 = true };
            var deviceInfo = new DeviceCreateInfo { SType = StructureType.DeviceCreateInfo, PNext = &enabled13, QueueCreateInfoCount = 1, PQueueCreateInfos = &queueInfo, PEnabledFeatures = &enabled };
            Check(api.CreateDevice(physical, &deviceInfo, null, &device), "CreateDevice");
            DeviceCaps caps = ReadCaps(properties.Properties.Limits, properties13.MaxBufferSize, enabled);
            return new(api, instance, device, physical, caps, enabled.ImageCubeArray, queueFamily);
        }
        catch
        {
            if (device.Handle != 0)
            {
                api.DestroyDevice(device, null);
            }

            if (instance.Handle != 0)
            {
                api.DestroyInstance(instance, null);
            }

            api.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        var program = new VulkanGraphicsPipeline(this, desc);
        _pipelineCount++;
        return program;
    }

    /// <inheritdoc />
    public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        var program = new VulkanComputePipeline(this, desc);
        _pipelineCount++;
        return program;
    }

    /// <inheritdoc />
    public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(desc);
        var commands = new VulkanCommandBuffer(this);
        _commandCount++;
        return commands;
    }

    /// <inheritdoc />
    public TextureCopyLayout GetTextureCopyLayout(TextureFormat format)
    {
        ValidateAlive();
        if (!Enum.IsDefined(format))
        {
            throw new NotSupportedException("Unknown color texture format.");
        }

        return new() { BytesPerTexel = 4, BufferOffsetAlignmentInBytes = 4, BytesPerRowAlignment = 4 };
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

        var buffer = new VulkanBuffer<T>(this, desc, layout, size);
        _bufferCount++;
        return buffer;
    }

    /// <inheritdoc />
    public IGraphicsTexture CreateTexture(TextureDesc desc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TextureValidation.Validate(desc, Caps);
        var texture = new VulkanTexture(this, desc);
        _textureCount++;
        return texture;
    }

    /// <inheritdoc />
    public IGraphicsSampler CreateSampler(SamplerDesc desc)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SamplerValidation.Validate(desc, Caps);
        var sampler = new VulkanSampler(this, desc);
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

        var table = new VulkanArgumentTable(this, desc);
        _argumentTableCount++;
        return table;
    }

    /// <inheritdoc />
    public IGraphicsShader CreateShader(ShaderArtifact artifact)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(artifact);
        var shader = new VulkanShader(this, artifact);
        _shaderCount++;
        return shader;
    }

    /// <summary>Destroys the logical device and instance; subsequent calls do nothing.</summary>
    public void Dispose()
    {
        if (_bufferCount != 0 || _textureCount != 0 || _samplerCount != 0 || _argumentTableCount != 0 || _pipelineCount != 0 || _shaderCount != 0 || _commandCount != 0 || _submissionCount != 0)
        {
            throw new InvalidOperationException("Dispose all argument tables, buffers, textures, samplers, shaders, pipelines, commands and submissions before disposing their device.");
        }

        if (_disposed)
        {
            return;
        }

        _api.DestroyDevice(_device, null);
        _api.DestroyInstance(_instance, null);
        _api.Dispose();
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

    internal void ReleaseTexture() => _textureCount--;

    internal void ReleaseBuffer() => _bufferCount--;

    private static PhysicalDevice SelectPhysicalDevice(Vk api, Instance instance, uint index)
    {
        uint count = 0;
        Check(api.EnumeratePhysicalDevices(instance, &count, null), "EnumeratePhysicalDevices");
        if (count == 0)
        {
            throw new NotSupportedException("No Vulkan adapter is available.");
        }

        var devices = new PhysicalDevice[count];
        fixed (PhysicalDevice* pointer = devices)
        {
            Check(api.EnumeratePhysicalDevices(instance, &count, pointer), "EnumeratePhysicalDevices");
        }

        if (index >= count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return devices[index];
    }

    private static uint SelectQueueFamily(Vk api, PhysicalDevice physical)
    {
        uint count = 0;
        api.GetPhysicalDeviceQueueFamilyProperties(physical, &count, null);
        var families = new QueueFamilyProperties[count];
        fixed (QueueFamilyProperties* pointer = families)
        {
            api.GetPhysicalDeviceQueueFamilyProperties(physical, &count, pointer);
        }

        for (uint i = 0; i < count; i++)
        {
            if (families[i].QueueCount != 0 && (families[i].QueueFlags & (QueueFlags.GraphicsBit | QueueFlags.ComputeBit)) == (QueueFlags.GraphicsBit | QueueFlags.ComputeBit))
            {
                return i;
            }
        }

        throw new NotSupportedException("A Vulkan queue supporting both graphics and compute is required.");
    }

    private static DeviceCaps ReadCaps(PhysicalDeviceLimits limits, ulong maxBufferSize, PhysicalDeviceFeatures enabled)
    {
        GraphicsFeatures features = GraphicsFeatures.IndirectDraw;
        if (enabled.SamplerAnisotropy)
        {
            features |= GraphicsFeatures.AnisotropicFiltering;
        }

        if (enabled.DepthBiasClamp)
        {
            features |= GraphicsFeatures.DepthBiasClamp;
        }

        return new()
        {
            ShaderTarget = ShaderTarget.SpirV,
            Features = features,
            MaxBufferSize = maxBufferSize,
            MaxStorageBufferBindingSize = limits.MaxStorageBufferRange,
            MaxTextureDimension2D = limits.MaxImageDimension2D,
            MaxTextureArrayLayers = limits.MaxImageArrayLayers,
            MaxColorAttachments = limits.MaxColorAttachments,
            MaxSampledTexturesPerStage = limits.MaxPerStageDescriptorSampledImages,
            MaxSamplersPerStage = limits.MaxPerStageDescriptorSamplers,
            MaxSamplerAnisotropy = enabled.SamplerAnisotropy ? checked((ushort)Math.Min(16, Math.Floor(limits.MaxSamplerAnisotropy))) : (ushort)1,
            MaxUniformBuffersPerStage = limits.MaxPerStageDescriptorUniformBuffers,
            MaxStorageBuffersPerStage = limits.MaxPerStageDescriptorStorageBuffers,
            MaxComputeWorkgroupsPerDimension = Math.Min(limits.MaxComputeWorkGroupCount[0], Math.Min(limits.MaxComputeWorkGroupCount[1], limits.MaxComputeWorkGroupCount[2])),
            MaxComputeWorkgroupSizeX = limits.MaxComputeWorkGroupSize[0],
            MaxComputeWorkgroupSizeY = limits.MaxComputeWorkGroupSize[1],
            MaxComputeWorkgroupSizeZ = limits.MaxComputeWorkGroupSize[2],
            MaxComputeInvocationsPerWorkgroup = limits.MaxComputeWorkGroupInvocations,
            CopyBufferOffsetAlignment = 1,
            CopyBufferSizeAlignment = 1,
            CopyBytesPerRowAlignment = 1,
            StorageBufferOffsetAlignment = checked((uint)limits.MinStorageBufferOffsetAlignment),
        };
    }

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {operation} failed: {result}.");
        }
    }
}
