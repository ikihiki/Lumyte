using Lumyte.Graphics.Abstractions;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Lumyte.Graphics.Vulkan;

/// <summary>Owns a Vulkan 1.3 instance and logical device with cached capabilities.</summary>
public sealed unsafe class VulkanDevice : IGraphicDevice, IDisposable
{
    private readonly Vk _api;
    private readonly Instance _instance;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Queue _nativeQueue;
    private readonly VulkanPresentation? _presentation;
    private int _surfaceCount;
    private int _semaphoreCount;
    private int _bufferCount;
    private int _textureCount;
    private int _samplerCount;
    private int _argumentTableCount;
    private int _shaderCount;
    private int _pipelineCount;
    private int _commandCount;
    private int _submissionCount;
    private bool _disposed;

    private VulkanDevice(Vk api, Instance instance, Device device, PhysicalDevice physicalDevice, DeviceCaps caps, bool supportsCubeArrays, uint queueFamily, bool cacheGraphicsPipelines, VulkanPresentation? presentation = null)
    {
        (_api, _instance, _device, Caps) = (api, instance, device, caps);
        _physicalDevice = physicalDevice;
        _presentation = presentation;
        SupportsCubeArrays = supportsCubeArrays;
        CacheGraphicsPipelines = cacheGraphicsPipelines;
        QueueFamily = queueFamily;
        api.GetDeviceQueue(device, queueFamily, 0, out _nativeQueue);
        Queue = new VulkanQueue(this);
    }

    /// <inheritdoc />
    public IGraphicsQueue Queue { get; }

    /// <summary>Gets the enabled capabilities and physical device limits captured during creation.</summary>
    public DeviceCaps Caps { get; }

    internal bool CacheGraphicsPipelines { get; }

    internal uint QueueFamily { get; }

    internal Queue NativeQueue => _nativeQueue;

    internal bool SupportsCubeArrays { get; }

    internal Vk Api => _api;

    internal Device NativeDevice => _device;

    internal Instance NativeInstance => _instance;

    internal VulkanPresentation Presentation => _presentation ?? throw new NotSupportedException("Enable presentation support in VulkanDeviceDesc when creating the device.");

    internal PhysicalDevice PhysicalDevice => _physicalDevice;

    /// <summary>Creates a Vulkan 1.3 device with a general queue, maintenance4, dynamic rendering and synchronization2.</summary>
    /// <param name="physicalDeviceIndex">The zero-based device index in the Vulkan enumeration.</param>
    /// <param name="cacheGraphicsPipelines">Whether to reuse native graphics pipelines for equivalent draw state.</param>
    /// <returns>The owned instance and logical device.</returns>
    public static VulkanDevice Create(uint physicalDeviceIndex = 0, bool cacheGraphicsPipelines = true)
        => Create(new VulkanDeviceDesc { PhysicalDeviceIndex = physicalDeviceIndex, CacheGraphicsPipelines = cacheGraphicsPipelines });

    /// <summary>Creates an instance and device without creating or inspecting any presentation target.</summary>
    /// <param name="desc">Device selection, platform instance extensions and optional presentation support.</param>
    /// <returns>The owned device; surfaces are created separately.</returns>
    public static VulkanDevice Create(VulkanDeviceDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.InstanceExtensions);
        string[] extensions = desc.InstanceExtensions.ToArray();
        if (extensions.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Supply valid Vulkan instance extension names.", nameof(desc));
        }

        return CreateCore(desc with { InstanceExtensions = Array.AsReadOnly(extensions) });
    }

    /// <summary>Creates independent surfaces on this existing instance and queue.</summary>
    /// <param name="factories">Nonempty callbacks returning newly owned surfaces in result order.</param>
    /// <returns>The owned surfaces; failure releases only surfaces created by this call.</returns>
    public IReadOnlyList<IGraphicsSurface> CreateSurfaces(IReadOnlyList<Func<Instance, SurfaceKHR>> factories)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(factories);
        Func<Instance, SurfaceKHR>[] snapshot = factories.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException("Supply at least one surface factory.", nameof(factories));
        }

        foreach (Func<Instance, SurfaceKHR> factory in snapshot)
        {
            ArgumentNullException.ThrowIfNull(factory);
        }

        _ = Presentation;
        var created = new List<IGraphicsSurface>();
        var handles = new HashSet<ulong>();
        try
        {
            foreach (Func<Instance, SurfaceKHR> factory in snapshot)
            {
                created.Add(CreateSurface(instance =>
                {
                    SurfaceKHR native = factory(instance);
                    if (!handles.Add(native.Handle))
                    {
                        throw new ArgumentException("Each factory must return a new, distinct native surface.", nameof(factories));
                    }

                    return native;
                }));
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

    /// <summary>Creates another graphics surface for this presentation-enabled instance and queue.</summary>
    /// <param name="createSurface">Creates a new surface with this instance; ownership transfers to the device.</param>
    /// <returns>The owned surface if the device's general queue supports it.</returns>
    public IGraphicsSurface CreateSurface(Func<Instance, SurfaceKHR> createSurface)
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(createSurface);
        _ = Presentation;
        SurfaceKHR native = createSurface(_instance);
        if (native.Handle == 0)
        {
            throw new ArgumentException("The surface factory returned a null native surface.", nameof(createSurface));
        }

        try
        {
            return new VulkanSurface(this, native);
        }
        catch
        {
            Presentation.Surface.DestroySurface(_instance, native, null);
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
    public IGraphicsSemaphore CreateSemaphore()
    {
        ValidateAlive();
        var semaphore = new VulkanSemaphore(this);
        _semaphoreCount++;
        return semaphore;
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
        return new() { BytesPerTexel = bytesPerTexel, BufferOffsetAlignmentInBytes = Math.Max(4U, bytesPerTexel), BytesPerRowAlignment = Math.Max(4U, bytesPerTexel) };
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

        var buffer = new VulkanBuffer<T>(this, desc, layout, size);
        _bufferCount++;
        return buffer;
    }

    /// <inheritdoc />
    public IGraphicsShaderDataBuffer<T> CreateBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
        where T : struct, IShaderData
    {
        ValidateAlive();
        ArgumentNullException.ThrowIfNull(artifact);
        var buffer = new VulkanShaderDataBuffer<T>(this, artifact, count, memory);
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
        if (_semaphoreCount != 0 || _surfaceCount != 0 || _bufferCount != 0 || _textureCount != 0 || _samplerCount != 0 || _argumentTableCount != 0 || _pipelineCount != 0 || _shaderCount != 0 || _commandCount != 0 || _submissionCount != 0)
        {
            throw new InvalidOperationException("Dispose all surfaces, semaphores, argument tables, buffers, textures, samplers, shaders, pipelines, commands and submissions before disposing their device.");
        }

        if (_disposed)
        {
            return;
        }

        _presentation?.Dispose();
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

    internal void RetainSurface() => _surfaceCount++;

    internal void ReleaseSurface() => _surfaceCount--;

    internal void ReleaseSemaphore() => _semaphoreCount--;

    internal void ReleaseTexture() => _textureCount--;

    internal void ReleaseBuffer() => _bufferCount--;

    private static VulkanDevice CreateCore(VulkanDeviceDesc desc)
    {
        var api = Vk.GetApi();
        Instance instance = default;
        Device device = default;
        bool presenting = desc.EnablePresentation;
        KhrSurface? surfaceApi = null;
        KhrSwapchain? swapchainApi = null;
        ExtSwapchainMaintenance1? maintenanceApi = null;
        nint instanceNames = 0;
        nint deviceNames = 0;
        try
        {
            var application = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version13 };
            string[] instanceExtensions = desc.InstanceExtensions.Concat(presenting ? new[] { "VK_KHR_surface", "VK_KHR_get_surface_capabilities2", "VK_EXT_surface_maintenance1" } : Array.Empty<string>()).Distinct().ToArray();
            instanceNames = instanceExtensions.Length == 0 ? 0 : SilkMarshal.StringArrayToPtr(instanceExtensions);
            var instanceInfo = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &application, EnabledExtensionCount = (uint)instanceExtensions.Length, PpEnabledExtensionNames = (byte**)instanceNames };
            Check(api.CreateInstance(&instanceInfo, null, &instance), "CreateInstance");
            if (presenting)
            {
                if (!api.TryGetInstanceExtension(instance, out surfaceApi))
                {
                    throw new NotSupportedException("Vulkan surface operations are unavailable.");
                }
            }

            PhysicalDevice physical = SelectPhysicalDevice(api, instance, desc.PhysicalDeviceIndex);
            var properties13 = new PhysicalDeviceVulkan13Properties { SType = StructureType.PhysicalDeviceVulkan13Properties };
            var properties = new PhysicalDeviceProperties2 { SType = StructureType.PhysicalDeviceProperties2, PNext = &properties13 };
            api.GetPhysicalDeviceProperties2(physical, &properties);
            if (properties.Properties.ApiVersion < Vk.Version13)
            {
                throw new NotSupportedException("The selected adapter must support Vulkan 1.3.");
            }

            var supportedMaintenance = new PhysicalDeviceSwapchainMaintenance1FeaturesEXT { SType = StructureType.PhysicalDeviceSwapchainMaintenance1FeaturesExt };
            var supported11 = new PhysicalDeviceVulkan11Features { SType = StructureType.PhysicalDeviceVulkan11Features, PNext = !presenting ? null : &supportedMaintenance };
            var supported13 = new PhysicalDeviceVulkan13Features { SType = StructureType.PhysicalDeviceVulkan13Features, PNext = &supported11 };
            var supported = new PhysicalDeviceFeatures2 { SType = StructureType.PhysicalDeviceFeatures2, PNext = &supported13 };
            api.GetPhysicalDeviceFeatures2(physical, &supported);
            if (!supported11.ShaderDrawParameters || !supported13.Maintenance4 || !supported13.DynamicRendering || !supported13.Synchronization2 || !supported.Features.IndependentBlend)
            {
                throw new NotSupportedException("Vulkan shader draw parameters, maintenance4, dynamic rendering, synchronization2 and independent blending are required.");
            }

            if (presenting && !supportedMaintenance.SwapchainMaintenance1)
            {
                throw new NotSupportedException("Vulkan swapchain maintenance1 is required for explicit image release and presentation fences.");
            }

            uint queueFamily = SelectQueueFamily(api, physical);
            float priority = 1;
            var queueInfo = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = queueFamily, QueueCount = 1, PQueuePriorities = &priority };
            var enabled = new PhysicalDeviceFeatures { SamplerAnisotropy = supported.Features.SamplerAnisotropy, DepthBiasClamp = supported.Features.DepthBiasClamp, ImageCubeArray = supported.Features.ImageCubeArray, IndependentBlend = true, ShaderInt64 = true };
            var enabledMaintenance = new PhysicalDeviceSwapchainMaintenance1FeaturesEXT { SType = StructureType.PhysicalDeviceSwapchainMaintenance1FeaturesExt, SwapchainMaintenance1 = true };
            var enabled11 = new PhysicalDeviceVulkan11Features { SType = StructureType.PhysicalDeviceVulkan11Features, ShaderDrawParameters = true, PNext = !presenting ? null : &enabledMaintenance };
            var enabled12 = new PhysicalDeviceVulkan12Features { SType = StructureType.PhysicalDeviceVulkan12Features, PNext = &enabled11, BufferDeviceAddress = true, RuntimeDescriptorArray = true, DescriptorBindingPartiallyBound = true, ShaderSampledImageArrayNonUniformIndexing = true };
            var enabled13 = new PhysicalDeviceVulkan13Features { SType = StructureType.PhysicalDeviceVulkan13Features, PNext = &enabled12, Maintenance4 = true, DynamicRendering = true, Synchronization2 = true };
            string[] deviceExtensions = !presenting ? [] : ["VK_KHR_swapchain", "VK_EXT_swapchain_maintenance1"];
            deviceNames = deviceExtensions.Length == 0 ? 0 : SilkMarshal.StringArrayToPtr(deviceExtensions);
            var deviceInfo = new DeviceCreateInfo { EnabledExtensionCount = (uint)deviceExtensions.Length, PpEnabledExtensionNames = (byte**)deviceNames, SType = StructureType.DeviceCreateInfo, PNext = &enabled13, QueueCreateInfoCount = 1, PQueueCreateInfos = &queueInfo, PEnabledFeatures = &enabled };
            Check(api.CreateDevice(physical, &deviceInfo, null, &device), "CreateDevice");
            DeviceCaps caps = ReadCaps(properties.Properties.Limits, properties13.MaxBufferSize, enabled);
            VulkanPresentation? presentation = null;
            if (presenting)
            {
                if (!api.TryGetDeviceExtension(instance, device, out swapchainApi) || !api.TryGetDeviceExtension(instance, device, out maintenanceApi))
                {
                    throw new NotSupportedException("Vulkan swapchain operations are unavailable.");
                }

                presentation = new(surfaceApi!, swapchainApi!, maintenanceApi!);
            }

            return new VulkanDevice(api, instance, device, physical, caps, enabled.ImageCubeArray, queueFamily, desc.CacheGraphicsPipelines, presentation);
        }
        catch
        {
            maintenanceApi?.Dispose();
            swapchainApi?.Dispose();
            surfaceApi?.Dispose();
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
        finally
        {
            if (instanceNames != 0)
            {
                SilkMarshal.Free(instanceNames);
            }

            if (deviceNames != 0)
            {
                SilkMarshal.Free(deviceNames);
            }
        }
    }

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

        throw new NotSupportedException("A Vulkan graphics/compute queue is required.");
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
