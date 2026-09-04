using Silk.NET.Vulkan;

using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkPipeline = Silk.NET.Vulkan.Pipeline;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Lumyte.Graphics.Vulkan;

/// <summary>A Vulkan device for explicit queues, resources, logical resource tables, and pipelines.</summary>
public sealed unsafe class VulkanDevice : IGpuBackend, IDisposable
{
    private const int MaximumRasterTextureDescriptors = 64;
    private const int MaximumRasterSamplerDescriptors = 64;
    private const int MaximumRasterBufferDescriptors = 64;

    private readonly Vk vk;
    private Instance instance;
    private PhysicalDevice physicalDevice;
    private Device device;
    private Queue queue;
    private CommandPool commandPool;
    private PhysicalDeviceMemoryProperties memoryProperties;
    private readonly Dictionary<ulong, MemoryRecord> memories = [];
    private readonly Dictionary<ulong, ImageRecord> images = [];
    private readonly Dictionary<ulong, ImageView> views = [];
    private readonly Dictionary<ulong, BufferRecord> buffers = [];
    private readonly Dictionary<ulong, GpuBufferView> bufferViews = [];
    private readonly Dictionary<ulong, PipelineRecord> pipelines = [];
    private readonly Dictionary<ulong, Sampler> samplers = [];
    private readonly Dictionary<nint, List<DescriptorSet>> commandDescriptorSets = [];
    private DescriptorSetLayout textureDescriptorLayout;
    private DescriptorSetLayout samplerDescriptorLayout;
    private DescriptorSetLayout shaderBufferDescriptorLayout;
    private DescriptorSetLayout computeBufferDescriptorLayout;
    private DescriptorPool descriptorPool;
    private uint lastImageMemoryTypeBits = uint.MaxValue;
    private uint lastBufferMemoryTypeBits = uint.MaxValue;
    private uint queueFamilyIndex;
    private ulong nextResourceId = 1;
    private bool disposed;

    private VulkanDevice(Vk vk)
    {
        this.vk = vk;
    }

    public string DeviceName { get; private set; } = string.Empty;
    public IGpuQueue MainQueue { get; private set; } = null!;
    public nint InstanceHandle => instance.Handle;
    public GpuBackendCapabilities Capabilities =>
        GpuBackendCapabilities.ExplicitPlacement
        | GpuBackendCapabilities.MemoryAliasing
        | GpuBackendCapabilities.RasterPipeline;

    public static VulkanDevice Create()
        => Create(0, null);

    public static VulkanDevice Create(uint extensionCount, byte** extensionNames)
    {
        var result = new VulkanDevice(Vk.GetApi());
        try
        {
            result.Initialize(extensionCount, extensionNames);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    internal Vk Api => vk;
    internal Instance Instance => instance;
    internal PhysicalDevice PhysicalDevice => physicalDevice;
    internal Device Device => device;
    internal Queue NativeQueue => queue;
    internal uint QueueFamilyIndex => queueFamilyIndex;
    internal CommandPool CommandPool => commandPool;

    public GpuTextureMemoryRequirements GetTextureMemoryRequirements(GpuTextureDescription description)
    {
        VerifyNotDisposed();
        description.Validate();
        Image image = CreateImage(description, aliasable: true);
        try
        {
            vk.GetImageMemoryRequirements(device, image, out MemoryRequirements requirements);
            lastImageMemoryTypeBits = requirements.MemoryTypeBits;
            return new(requirements.Size, requirements.Alignment, requirements.MemoryTypeBits);
        }
        finally
        {
            vk.DestroyImage(device, image, null);
        }
    }

    public GpuMemoryAllocation AllocateMemory(ulong size, ulong alignment, GpuMemoryKind kind)
    {
        uint compatibleTypes = kind switch
        {
            GpuMemoryKind.DeviceLocal => lastImageMemoryTypeBits,
            GpuMemoryKind.HostMapped or GpuMemoryKind.HostCached => lastBufferMemoryTypeBits,
            _ => uint.MaxValue,
        };
        return AllocateMemory(size, alignment, kind, compatibleTypes);
    }

    public GpuMemoryAllocation AllocateMemory(
        ulong size,
        ulong alignment,
        GpuMemoryKind kind,
        ulong compatibility)
    {
        VerifyNotDisposed();
        MemoryPropertyFlags flags = kind switch
        {
            GpuMemoryKind.DeviceLocal => MemoryPropertyFlags.DeviceLocalBit,
            GpuMemoryKind.HostMapped => MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            GpuMemoryKind.HostCached => MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCachedBit,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        uint compatibleTypes = compatibility == 0
            ? uint.MaxValue
            : compatibility <= uint.MaxValue
                ? (uint)compatibility
                : throw new ArgumentOutOfRangeException(nameof(compatibility));
        uint memoryType = FindMemoryType(compatibleTypes, flags);
        var createInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = size,
            MemoryTypeIndex = memoryType,
        };
        Check(vk.AllocateMemory(device, in createInfo, null, out DeviceMemory memory), "vkAllocateMemory");
        nint cpuAddress = 0;
        if (kind != GpuMemoryKind.DeviceLocal)
        {
            void* mapped = null;
            Check(vk.MapMemory(device, memory, 0, size, 0, &mapped), "vkMapMemory");
            cpuAddress = (nint)mapped;
        }

        memories.Add(memory.Handle, new(memory, size, kind, cpuAddress, memoryType));
        return new(size, alignment, kind, cpuAddress, new(memory.Handle, 0, size));
    }

    public bool TryCombineMemoryCompatibility(ulong left, ulong right, out ulong combined)
    {
        if (left > uint.MaxValue || right > uint.MaxValue)
        {
            combined = 0;
            return false;
        }
        ulong leftMask = left == 0 ? uint.MaxValue : left;
        ulong rightMask = right == 0 ? uint.MaxValue : right;
        combined = leftMask & rightMask;
        return combined != 0;
    }

    public ulong GetMemoryCompatibilityKey(GpuMemoryKind kind, ulong compatibility)
    {
        VerifyNotDisposed();
        if (compatibility == 0) { return 0; }
        if (compatibility > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(compatibility));
        }
        MemoryPropertyFlags flags = kind switch
        {
            GpuMemoryKind.DeviceLocal => MemoryPropertyFlags.DeviceLocalBit,
            GpuMemoryKind.HostMapped => MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            GpuMemoryKind.HostCached => MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCachedBit,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        uint memoryType = FindMemoryType((uint)compatibility, flags);
        return 1UL << checked((int)memoryType);
    }

    public bool IsMemoryCompatibilityKeyCompatible(
        GpuMemoryKind kind,
        ulong allocationKey,
        ulong requirement)
    {
        if (requirement == 0) { return true; }
        if (requirement > uint.MaxValue || allocationKey > uint.MaxValue)
        {
            return false;
        }
        return (allocationKey & requirement) != 0;
    }

    public void FreeMemory(GpuMemoryAllocation allocation)
    {
        VerifyNotDisposed();
        if (!memories.TryGetValue(allocation.MemoryAddress.AllocationId, out MemoryRecord? memory)
            || !memory.MatchesRoot(allocation))
        {
            throw new ArgumentException("Allocation does not belong to this Vulkan device.", nameof(allocation));
        }
        if (memory.BoundResourceCount != 0)
        {
            throw new InvalidOperationException("Allocation still has a live placed resource.");
        }
        memories.Remove(allocation.MemoryAddress.AllocationId);
        if (memory.CpuAddress != 0) { vk.UnmapMemory(device, memory.Memory); }
        vk.FreeMemory(device, memory.Memory, null);
    }

    public GpuTextureHandle CreatePlacedTexture(
        GpuTextureDescription description,
        GpuMemoryAllocation allocation)
    {
        VerifyNotDisposed();
        allocation.Validate();
        if (!memories.TryGetValue(allocation.MemoryAddress.AllocationId, out MemoryRecord? memory)
            || !memory.MatchesRegion(allocation))
        {
            throw new ArgumentException("Allocation does not belong to this Vulkan device.", nameof(allocation));
        }

        Image image = CreateImage(description, aliasable: true);
        try
        {
            vk.GetImageMemoryRequirements(device, image, out MemoryRequirements requirements);
            if (allocation.Size < requirements.Size
                || allocation.MemoryAddress.Offset % requirements.Alignment != 0
                || (requirements.MemoryTypeBits & (1u << checked((int)memory.MemoryType))) == 0)
            {
                throw new ArgumentException("Allocation is incompatible with Vulkan image requirements.", nameof(allocation));
            }

            Check(vk.BindImageMemory(device, image, memory.Memory, allocation.MemoryAddress.Offset), "vkBindImageMemory");
            var handle = new GpuTextureHandle(image.Handle);
            images.Add(handle.Value, new(
                image,
                description,
                ImageLayout.Undefined,
                true,
                allocation.MemoryAddress.AllocationId));
            memory.BoundResourceCount++;
            return handle;
        }
        catch
        {
            vk.DestroyImage(device, image, null);
            throw;
        }
    }

    public void DestroyTexture(GpuTextureHandle texture)
    {
        VerifyNotDisposed();
        if (!images.Remove(texture.Value, out ImageRecord? record))
        {
            throw new ArgumentException("Texture does not belong to this Vulkan device.", nameof(texture));
        }

        if (record.Owned)
        {
            vk.DestroyImage(device, record.Image, null);
            memories[record.AllocationId].BoundResourceCount--;
        }
    }

    public GpuTextureView CreateTextureView(
        GpuTextureHandle texture,
        GpuTextureViewDescription description)
    {
        VerifyNotDisposed();
        if (!images.TryGetValue(texture.Value, out ImageRecord? record))
        {
            throw new ArgumentException("Texture does not belong to this Vulkan device.", nameof(texture));
        }

        ImageAspectFlags aspect = Aspect(description.Format);
        uint mipCount = description.MipCount == uint.MaxValue
            ? record.Description.MipCount - description.BaseMip
            : description.MipCount;
        uint layerCount = description.LayerCount == uint.MaxValue
            ? record.Description.LayerCount - description.BaseLayer
            : description.LayerCount;
        var createInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = record.Image,
            ViewType = ImageViewType.Type2D,
            Format = ToVulkanFormat(description.Format),
            SubresourceRange = new(aspect, description.BaseMip, mipCount, description.BaseLayer, layerCount),
        };
        Check(vk.CreateImageView(device, in createInfo, null, out ImageView view), "vkCreateImageView");
        ulong id = NextResourceId();
        views.Add(id, view);
        return new(new(id), texture, description);
    }

    public void DestroyTextureView(GpuTextureView view)
    {
        VerifyNotDisposed();
        if (!views.Remove(view.Id.Value, out ImageView nativeView))
        {
            throw new ArgumentException("View does not belong to this Vulkan device.", nameof(view));
        }

        vk.DestroyImageView(device, nativeView, null);
    }

    public SamplerId CreateSampler(GpuSamplerDescription description)
    {
        VerifyNotDisposed();
        description.Validate();
        var createInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MinFilter = ToVulkanFilter(description.MinFilter),
            MagFilter = ToVulkanFilter(description.MagFilter),
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = ToVulkanAddressMode(description.AddressU),
            AddressModeV = ToVulkanAddressMode(description.AddressV),
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MaxLod = 0,
        };
        Check(vk.CreateSampler(device, in createInfo, null, out Sampler sampler), "vkCreateSampler");
        var handle = new SamplerId(NextResourceId());
        if (handle.IsNull || !samplers.TryAdd(handle.Value, sampler))
        {
            vk.DestroySampler(device, sampler, null);
            throw new InvalidOperationException("Vulkan returned an invalid or duplicate sampler handle.");
        }
        return handle;
    }

    public void DestroySampler(SamplerId sampler)
    {
        VerifyNotDisposed();
        if (!samplers.Remove(sampler.Value, out Sampler nativeSampler))
        {
            throw new ArgumentException("Sampler does not belong to this Vulkan device.", nameof(sampler));
        }
        vk.DestroySampler(device, nativeSampler, null);
    }

    internal GpuTextureView RegisterSwapchainImage(Image image, GpuTextureDescription description, ImageView view)
    {
        var texture = new GpuTextureHandle(image.Handle);
        images.Add(texture.Value, new(image, description, ImageLayout.Undefined, false));
        ulong id = NextResourceId();
        views.Add(id, view);
        return new(new(id), texture, new(description.Format));
    }

    internal ImageView UnregisterSwapchainImage(GpuTextureView view)
    {
        if (!views.Remove(view.Id.Value, out ImageView nativeView)
            || !images.Remove(view.Texture.Value))
        {
            throw new ArgumentException(
                "Swapchain view does not belong to this Vulkan device.",
                nameof(view));
        }

        return nativeView;
    }

    internal ImageView ResolveImageView(GpuTextureView view)
        => views.TryGetValue(view.Id.Value, out ImageView nativeView)
            ? nativeView
            : throw new ArgumentException("View does not belong to this Vulkan device.", nameof(view));

    internal CommandBuffer PreparePresent(GpuCommandBuffer commands, GpuTextureHandle texture)
    {
        if (GpuBackendCommands.GetRecorder(commands) is not VulkanRecorder recorder || recorder.Owner != this)
        {
            throw new ArgumentException("Command buffer belongs to another backend.", nameof(commands));
        }

        if (!images.TryGetValue(texture.Value, out ImageRecord? image))
        {
            throw new ArgumentException("Swapchain image is not registered.", nameof(texture));
        }

        Transition(recorder.CommandBuffer, image.Image, image.Layout, ImageLayout.PresentSrcKhr,
            PipelineStageFlags2.ColorAttachmentOutputBit, AccessFlags2.ColorAttachmentWriteBit,
            PipelineStageFlags2.BottomOfPipeBit, 0);
        images[texture.Value] = image with { Layout = ImageLayout.PresentSrcKhr };
        return recorder.CommandBuffer;
    }

    public GpuBufferMemoryRequirements GetBufferMemoryRequirements(GpuBufferDescription description)
    {
        VerifyNotDisposed();
        description.Validate();
        VkBuffer buffer = CreateBuffer(description);
        try
        {
            vk.GetBufferMemoryRequirements(device, buffer, out MemoryRequirements requirements);
            lastBufferMemoryTypeBits = requirements.MemoryTypeBits;
            return new(requirements.Size, requirements.Alignment, requirements.MemoryTypeBits);
        }
        finally
        {
            vk.DestroyBuffer(device, buffer, null);
        }
    }

    public GpuBufferHandle CreatePlacedBuffer(
        GpuBufferDescription description,
        GpuMemoryAllocation allocation)
    {
        VerifyNotDisposed();
        allocation.Validate();
        if (!memories.TryGetValue(allocation.MemoryAddress.AllocationId, out MemoryRecord? memory)
            || !memory.MatchesRegion(allocation))
        {
            throw new ArgumentException("Allocation does not belong to this Vulkan device.", nameof(allocation));
        }

        VkBuffer buffer = CreateBuffer(description);
        try
        {
            vk.GetBufferMemoryRequirements(device, buffer, out MemoryRequirements requirements);
            if (allocation.Size < requirements.Size
                || allocation.MemoryAddress.Offset % requirements.Alignment != 0
                || (requirements.MemoryTypeBits & (1u << checked((int)memory.MemoryType))) == 0)
            {
                throw new ArgumentException("Allocation is incompatible with Vulkan buffer requirements.", nameof(allocation));
            }

            Check(vk.BindBufferMemory(device, buffer, memory.Memory, allocation.MemoryAddress.Offset), "vkBindBufferMemory");
            var handle = new GpuBufferHandle(buffer.Handle, description.Size);
            buffers.Add(handle.Value, new(
                buffer,
                description,
                allocation.MemoryAddress.AllocationId,
                allocation.MemoryAddress.Offset));
            memory.BoundResourceCount++;
            return handle;
        }
        catch
        {
            vk.DestroyBuffer(device, buffer, null);
            throw;
        }
    }

    public void DestroyBuffer(GpuBufferHandle buffer)
    {
        VerifyNotDisposed();
        if (!buffers.TryGetValue(buffer.Value, out BufferRecord? record))
        {
            throw new ArgumentException("Buffer does not belong to this Vulkan device.", nameof(buffer));
        }
        if (bufferViews.Values.Any(view => view.Buffer == buffer))
        {
            throw new InvalidOperationException("Buffer still has a live view.");
        }

        buffers.Remove(buffer.Value);
        vk.DestroyBuffer(device, record.Buffer, null);
        memories[record.AllocationId].BoundResourceCount--;
    }

    public void WriteBuffer(GpuBufferHandle buffer, ReadOnlySpan<byte> source)
        => WriteBuffer(buffer, 0, source);

    public void WriteBuffer(
        GpuBufferHandle buffer,
        ulong destinationOffset,
        ReadOnlySpan<byte> source)
    {
        VerifyNotDisposed();
        if (!buffers.TryGetValue(buffer.Value, out BufferRecord? record)
            || record.Description.Size != buffer.Size)
        {
            throw new ArgumentException("Buffer does not belong to this Vulkan device.", nameof(buffer));
        }
        MemoryRecord memory = memories[record.AllocationId];
        if (memory.Kind != GpuMemoryKind.HostMapped)
        {
            throw new ArgumentException("Buffer memory is not host writable.", nameof(buffer));
        }
        if (source.IsEmpty
            || (destinationOffset & 3) != 0
            || (source.Length & 3) != 0
            || destinationOffset > record.Description.Size
            || checked((ulong)source.Length) > record.Description.Size - destinationOffset)
        {
            throw new ArgumentException(
                "The destination and source must be non-empty, four-byte aligned, and fit the buffer.",
                nameof(source));
        }

        nint address = checked(memory.CpuAddress + (nint)record.AllocationOffset + (nint)destinationOffset);
        source.CopyTo(new Span<byte>((void*)address, source.Length));
    }

    public GpuBufferView CreateBufferView(
        GpuBufferHandle buffer,
        GpuBufferViewDescription description)
    {
        VerifyNotDisposed();
        if (!buffers.TryGetValue(buffer.Value, out BufferRecord? record)
            || record.Description.Size != buffer.Size)
        {
            throw new ArgumentException("Buffer does not belong to this Vulkan device.", nameof(buffer));
        }
        if ((record.Description.Usage & GpuBufferUsage.ShaderData) == 0)
        {
            throw new ArgumentException("Buffer views require ShaderData usage.", nameof(buffer));
        }
        GpuBufferViewDescription normalized = description.Normalize(buffer);
        var view = new GpuBufferView(new(NextResourceId()), buffer, normalized);
        bufferViews.Add(view.Id.Value, view);
        return view;
    }

    public void DestroyBufferView(GpuBufferView view)
    {
        VerifyNotDisposed();
        if (!bufferViews.Remove(view.Id.Value, out GpuBufferView registered) || registered != view)
        {
            if (!registered.Id.IsNull) { bufferViews.Add(registered.Id.Value, registered); }
            throw new ArgumentException("Buffer view does not belong to this Vulkan device.", nameof(view));
        }
    }

    public GpuRasterPipelineHandle CreateRasterPipeline(
        GpuRasterPipelineDescription description,
        GpuShaderPackage package,
        string vertexEntryPoint,
        string pixelEntryPoint,
        ReadOnlyMemory<byte> expectedAbiHash)
    {
        VerifyNotDisposed();
        description.Validate();
        ArgumentNullException.ThrowIfNull(package);
        if (description.ColorTargets.Count != 1 || description.SampleCount != 1)
        {
            throw new NotSupportedException("The current Vulkan raster slice supports one single-sampled color target.");
        }
        if (description.AlphaToCoverage || description.SupportsDualSourceBlending)
        {
            throw new NotSupportedException("The current Vulkan raster slice does not support alpha-to-coverage or dual-source blending.");
        }
        if (description.DepthFormat is { } depth && description.StencilFormat is { } stencil && depth != stencil)
        {
            throw new NotSupportedException("Vulkan uses one combined depth-stencil attachment in this raster slice.");
        }
        GpuShaderBinary vertexShader = package.Select(
            GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, vertexEntryPoint, expectedAbiHash.Span).ToBinary();
        GpuShaderBinary pixelShader = package.Select(
            GpuShaderCodeFormat.SpirV, GpuShaderStage.Pixel, pixelEntryPoint, expectedAbiHash.Span).ToBinary();
        ShaderModule vertex = CreateShaderModule(vertexShader.Bytes.Span);
        ShaderModule pixel = CreateShaderModule(pixelShader.Bytes.Span);
        PipelineLayout layout = default;
        try
        {
            DescriptorSetLayout* descriptorLayouts = stackalloc DescriptorSetLayout[3]
            {
                textureDescriptorLayout,
                samplerDescriptorLayout,
                shaderBufferDescriptorLayout,
            };
            var pushConstantRange = new PushConstantRange(
                ShaderStageFlags.AllGraphics,
                0,
                GpuShaderBindingConvention.RootDataSize);
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 3,
                PSetLayouts = descriptorLayouts,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushConstantRange,
            };
            Check(vk.CreatePipelineLayout(device, in layoutInfo, null, out layout), "vkCreatePipelineLayout");
            byte[] entryPoint = "main\0"u8.ToArray();
            fixed (byte* entry = entryPoint)
            {
                PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[2];
                stages[0] = new()
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.VertexBit,
                    Module = vertex,
                    PName = entry,
                };
                stages[1] = new()
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.FragmentBit,
                    Module = pixel,
                    PName = entry,
                };
                var vertexInput = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };
                var assembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = description.Topology == GpuPrimitiveTopology.TriangleStrip
                        ? PrimitiveTopology.TriangleStrip
                        : PrimitiveTopology.TriangleList,
                };
                var viewport = new PipelineViewportStateCreateInfo
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    ScissorCount = 1,
                };
                var raster = new PipelineRasterizationStateCreateInfo
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    PolygonMode = PolygonMode.Fill,
                    CullMode = description.CullMode switch
                    {
                        GpuCullMode.None => CullModeFlags.None,
                        GpuCullMode.Front => CullModeFlags.FrontBit,
                        GpuCullMode.Back => CullModeFlags.BackBit,
                        _ => throw new ArgumentOutOfRangeException(nameof(description)),
                    },
                    FrontFace = description.FrontFace == GpuFrontFace.Clockwise
                        ? FrontFace.Clockwise
                        : FrontFace.CounterClockwise,
                    LineWidth = 1,
                };
                var multisample = new PipelineMultisampleStateCreateInfo
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    RasterizationSamples = SampleCountFlags.Count1Bit,
                };
                GpuColorTargetDescription colorTarget = description.ColorTargets[0];
                GpuBlendDescription? blendDescription = description.EmbeddedBlend;
                var blendAttachment = new PipelineColorBlendAttachmentState
                {
                    BlendEnable = blendDescription is not null,
                    SrcColorBlendFactor = ToVulkanBlendFactor(blendDescription?.SourceColorFactor ?? GpuBlendFactor.One),
                    DstColorBlendFactor = ToVulkanBlendFactor(blendDescription?.DestinationColorFactor ?? GpuBlendFactor.Zero),
                    ColorBlendOp = ToVulkanBlendOperation(blendDescription?.ColorOperation ?? GpuBlendOperation.Add),
                    SrcAlphaBlendFactor = ToVulkanBlendFactor(blendDescription?.SourceAlphaFactor ?? GpuBlendFactor.One),
                    DstAlphaBlendFactor = ToVulkanBlendFactor(blendDescription?.DestinationAlphaFactor ?? GpuBlendFactor.Zero),
                    AlphaBlendOp = ToVulkanBlendOperation(blendDescription?.AlphaOperation ?? GpuBlendOperation.Add),
                    ColorWriteMask = ToVulkanColorWriteMask(
                        colorTarget.WriteMask & (blendDescription?.ColorWriteMask ?? GpuColorWriteMask.All)),
                };
                var blend = new PipelineColorBlendStateCreateInfo
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    AttachmentCount = 1,
                    PAttachments = &blendAttachment,
                };
                DynamicState* dynamicStates = stackalloc DynamicState[2] { DynamicState.Viewport, DynamicState.Scissor };
                var dynamic = new PipelineDynamicStateCreateInfo
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = 2,
                    PDynamicStates = dynamicStates,
                };
                var stencilState = new StencilOpState(
                    StencilOp.Keep,
                    StencilOp.Keep,
                    StencilOp.Keep,
                    CompareOp.Always,
                    byte.MaxValue,
                    byte.MaxValue,
                    0);
                var depthStencil = new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = description.DepthFormat is not null,
                    DepthWriteEnable = description.DepthFormat is not null,
                    DepthCompareOp = CompareOp.LessOrEqual,
                    StencilTestEnable = description.StencilFormat is not null,
                    Front = stencilState,
                    Back = stencilState,
                };
                Format[] colorFormats = [.. description.ColorTargets.Select(target => ToVulkanFormat(target.Format))];
                fixed (Format* colorFormatPointer = colorFormats)
                {
                var rendering = new PipelineRenderingCreateInfo
                {
                    SType = StructureType.PipelineRenderingCreateInfo,
                    ColorAttachmentCount = (uint)colorFormats.Length,
                    PColorAttachmentFormats = colorFormatPointer,
                    DepthAttachmentFormat = description.DepthFormat is { } depthFormat
                        ? ToVulkanFormat(depthFormat)
                        : default,
                    StencilAttachmentFormat = description.StencilFormat is { } stencilFormat
                        ? ToVulkanFormat(stencilFormat)
                        : default,
                };
                var create = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    PNext = &rendering,
                    StageCount = 2,
                    PStages = stages,
                    PVertexInputState = &vertexInput,
                    PInputAssemblyState = &assembly,
                    PViewportState = &viewport,
                    PRasterizationState = &raster,
                    PMultisampleState = &multisample,
                    PColorBlendState = &blend,
                    PDepthStencilState = description.DepthFormat is null && description.StencilFormat is null
                        ? null
                        : &depthStencil,
                    PDynamicState = &dynamic,
                    Layout = layout,
                };
                Check(vk.CreateGraphicsPipelines(device, default, 1, in create, null, out VkPipeline pipeline), "vkCreateGraphicsPipelines");
                var handle = new GpuRasterPipelineHandle(pipeline.Handle);
                pipelines.Add(handle.Value, new(pipeline, layout));
                layout = default;
                return handle;
                }
            }
        }
        finally
        {
            vk.DestroyShaderModule(device, vertex, null);
            vk.DestroyShaderModule(device, pixel, null);
            if (layout.Handle != 0) { vk.DestroyPipelineLayout(device, layout, null); }
        }
    }

    public void DestroyRasterPipeline(GpuRasterPipelineHandle pipeline)
    {
        VerifyNotDisposed();
        if (!pipelines.Remove(pipeline.Value, out PipelineRecord? record))
        {
            throw new ArgumentException("Pipeline does not belong to this Vulkan device.", nameof(pipeline));
        }

        vk.DestroyPipeline(device, record.Pipeline, null);
        vk.DestroyPipelineLayout(device, record.Layout, null);
    }

    public GpuComputePipelineHandle CreateComputePipeline(
        GpuShaderPackage package,
        string entryPoint,
        ReadOnlyMemory<byte> expectedAbiHash)
    {
        VerifyNotDisposed();
        ArgumentNullException.ThrowIfNull(package);
        GpuShaderBinary shader = package.Select(
            GpuShaderCodeFormat.SpirV, GpuShaderStage.Compute, entryPoint, expectedAbiHash.Span).ToBinary();
        ShaderModule module = CreateShaderModule(shader.Bytes.Span);
        PipelineLayout layout = default;
        try
        {
            DescriptorSetLayout descriptorLayout = computeBufferDescriptorLayout;
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = &descriptorLayout,
            };
            Check(vk.CreatePipelineLayout(device, in layoutInfo, null, out layout), "vkCreatePipelineLayout");

            byte[] nativeEntryPoint = "main\0"u8.ToArray();
            fixed (byte* nativeEntry = nativeEntryPoint)
            {
                var stage = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.ComputeBit,
                    Module = module,
                    PName = nativeEntry,
                };
                var create = new ComputePipelineCreateInfo
                {
                    SType = StructureType.ComputePipelineCreateInfo,
                    Stage = stage,
                    Layout = layout,
                };
                Check(vk.CreateComputePipelines(
                    device, default, 1, in create, null, out VkPipeline pipeline),
                    "vkCreateComputePipelines");
                var handle = new GpuComputePipelineHandle(pipeline.Handle);
                pipelines.Add(handle.Value, new(pipeline, layout));
                layout = default;
                return handle;
            }
        }
        finally
        {
            vk.DestroyShaderModule(device, module, null);
            if (layout.Handle != 0) { vk.DestroyPipelineLayout(device, layout, null); }
        }
    }

    public void DestroyComputePipeline(GpuComputePipelineHandle pipeline)
    {
        VerifyNotDisposed();
        if (!pipelines.Remove(pipeline.Value, out PipelineRecord? record))
        {
            throw new ArgumentException("Pipeline does not belong to this Vulkan device.", nameof(pipeline));
        }

        vk.DestroyPipeline(device, record.Pipeline, null);
        vk.DestroyPipelineLayout(device, record.Layout, null);
    }

    public GpuMemoryAddress GetBufferMemoryAddress(GpuBufferHandle buffer, ulong offset, ulong length)
    {
        VerifyNotDisposed();
        if (!buffers.TryGetValue(buffer.Value, out BufferRecord? record))
        {
            throw new ArgumentException("Buffer does not belong to this Vulkan device.", nameof(buffer));
        }

        if (offset > record.Description.Size || length > record.Description.Size - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return new(record.AllocationId, checked(record.AllocationOffset + offset), length);
    }

    private void Submit(ReadOnlySpan<GpuCommandBuffer> commands, VulkanSemaphore signal, ulong signalValue)
    {
        VerifyNotDisposed();
        if (commands.Length == 0)
        {
            throw new ArgumentException("At least one command buffer is required.", nameof(commands));
        }
        signal.ValidateSignal(signalValue);

        CommandBuffer[] native = new CommandBuffer[commands.Length];
        for (int index = 0; index < commands.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(commands[index]);
            if (GpuBackendCommands.Finish(commands[index]) is not VulkanRecorder recorder || recorder.Owner != this)
            {
                throw new ArgumentException("Command buffer belongs to another backend.", nameof(commands));
            }

            native[index] = recorder.CommandBuffer;
        }

        ulong value = signalValue;
        VkSemaphore semaphore = signal.Handle;
        var timeline = new TimelineSemaphoreSubmitInfo
        {
            SType = StructureType.TimelineSemaphoreSubmitInfo,
            SignalSemaphoreValueCount = 1,
            PSignalSemaphoreValues = &value,
        };
        fixed (CommandBuffer* commandBuffers = native)
        {
            var submit = new SubmitInfo
            {
                SType = StructureType.SubmitInfo,
                PNext = &timeline,
                CommandBufferCount = (uint)native.Length,
                PCommandBuffers = commandBuffers,
                SignalSemaphoreCount = 1,
                PSignalSemaphores = &semaphore,
            };
            Check(vk.QueueSubmit(queue, 1, in submit, default), "vkQueueSubmit");
        }
        signal.Track(signalValue, native);
    }

    private void Wait(VulkanSemaphore semaphore, ulong value)
    {
        VerifyNotDisposed();
        semaphore.ValidateWait(value);
        VkSemaphore handle = semaphore.Handle;
        var wait = new SemaphoreWaitInfo
        {
            SType = StructureType.SemaphoreWaitInfo,
            SemaphoreCount = 1,
            PSemaphores = &handle,
            PValues = &value,
        };
        Check(vk.WaitSemaphores(device, in wait, ulong.MaxValue), "vkWaitSemaphores");
        semaphore.ReleaseCompleted(value);
    }

    private bool IsComplete(VulkanSemaphore semaphore, ulong value)
    {
        VerifyNotDisposed();
        semaphore.ValidateWait(value);
        Check(vk.GetSemaphoreCounterValue(device, semaphore.Handle, out ulong completed),
            "vkGetSemaphoreCounterValue");
        if (completed < value) { return false; }
        semaphore.ReleaseCompleted(completed);
        return true;
    }

    private void RecordBarrier(CommandBuffer commandBuffer, GpuStage before, GpuStage after, GpuBarrierHazards hazards)
    {
        var barrier = new MemoryBarrier2
        {
            SType = StructureType.MemoryBarrier2,
            SrcStageMask = Stage(before),
            SrcAccessMask = AccessForProducer(before),
            DstStageMask = Stage(after),
            DstAccessMask = AccessForConsumer(after, hazards),
        };
        var dependency = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            MemoryBarrierCount = 1,
            PMemoryBarriers = &barrier,
        };
        vk.CmdPipelineBarrier2(commandBuffer, in dependency);
    }

    private void RecordBeginRendering(CommandBuffer commandBuffer, IReadOnlyList<GpuColorAttachment> colors, GpuDepthStencilAttachment? depth)
    {
        if (colors.Count != 1)
        {
            throw new NotSupportedException("The current Vulkan slice supports one color attachment.");
        }

        GpuColorAttachment attachment = colors[0];
        if (!images.TryGetValue(attachment.View.Texture.Value, out ImageRecord? record)
            || !views.TryGetValue(attachment.View.Id.Value, out ImageView view))
        {
            throw new ArgumentException("Rendering attachment does not belong to this Vulkan device.", nameof(colors));
        }

        Transition(commandBuffer, record.Image, record.Layout, ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlags2.TopOfPipeBit, 0, PipelineStageFlags2.ColorAttachmentOutputBit,
            AccessFlags2.ColorAttachmentReadBit | AccessFlags2.ColorAttachmentWriteBit);
        images[attachment.View.Texture.Value] = record with { Layout = ImageLayout.ColorAttachmentOptimal };

        var nativeAttachment = new RenderingAttachmentInfo
        {
            SType = StructureType.RenderingAttachmentInfo,
            ImageView = view,
            ImageLayout = ImageLayout.ColorAttachmentOptimal,
            LoadOp = ToLoadOperation(attachment.LoadOperation),
            StoreOp = attachment.StoreOperation == GpuAttachmentStoreOperation.Store
                ? AttachmentStoreOp.Store : AttachmentStoreOp.DontCare,
            ClearValue = new ClearValue
            {
                Color = new(
                    attachment.ClearColor.Red,
                    attachment.ClearColor.Green,
                    attachment.ClearColor.Blue,
                    attachment.ClearColor.Alpha),
            },
        };
        RenderingAttachmentInfo nativeDepthAttachment = default;
        RenderingAttachmentInfo* depthPointer = null;
        RenderingAttachmentInfo* stencilPointer = null;
        if (depth is { } depthAttachment)
        {
            if (!images.TryGetValue(depthAttachment.View.Texture.Value, out ImageRecord? depthRecord)
                || !views.TryGetValue(depthAttachment.View.Id.Value, out ImageView depthView))
            {
                throw new ArgumentException("Depth-stencil attachment does not belong to this Vulkan device.", nameof(depth));
            }
            if (depthRecord.Description.Width != record.Description.Width
                || depthRecord.Description.Height != record.Description.Height)
            {
                throw new ArgumentException("Depth-stencil attachment dimensions must match the color attachment.", nameof(depth));
            }

            GpuFormat depthFormat = depthAttachment.View.Description.Format;
            ImageAspectFlags depthAspect = Aspect(depthFormat);
            Transition(
                commandBuffer,
                depthRecord.Image,
                depthRecord.Layout,
                ImageLayout.DepthStencilAttachmentOptimal,
                PipelineStageFlags2.TopOfPipeBit,
                0,
                PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit,
                AccessFlags2.DepthStencilAttachmentReadBit | AccessFlags2.DepthStencilAttachmentWriteBit,
                depthAspect);
            images[depthAttachment.View.Texture.Value] = depthRecord with
            {
                Layout = ImageLayout.DepthStencilAttachmentOptimal,
            };
            nativeDepthAttachment = new()
            {
                SType = StructureType.RenderingAttachmentInfo,
                ImageView = depthView,
                ImageLayout = ImageLayout.DepthStencilAttachmentOptimal,
                LoadOp = ToLoadOperation(depthAttachment.LoadOperation),
                StoreOp = depthAttachment.StoreOperation == GpuAttachmentStoreOperation.Store
                    ? AttachmentStoreOp.Store : AttachmentStoreOp.DontCare,
                ClearValue = new ClearValue
                {
                    DepthStencil = new(
                        depthAttachment.ClearValue.Depth,
                        depthAttachment.ClearValue.Stencil),
                },
            };
            if (GpuBackendCommands.HasDepth(depthFormat)) { depthPointer = &nativeDepthAttachment; }
            if (GpuBackendCommands.HasStencil(depthFormat)) { stencilPointer = &nativeDepthAttachment; }
        }

        var rendering = new RenderingInfo
        {
            SType = StructureType.RenderingInfo,
            RenderArea = new(new(0, 0), new(record.Description.Width, record.Description.Height)),
            LayerCount = 1,
            ColorAttachmentCount = 1,
            PColorAttachments = &nativeAttachment,
            PDepthAttachment = depthPointer,
            PStencilAttachment = stencilPointer,
        };
        vk.CmdBeginRendering(commandBuffer, in rendering);
    }

    private void RecordCopy(CommandBuffer commandBuffer, GpuTextureHandle source, GpuMemoryAddress destination, GpuTextureCopyFootprint footprint)
    {
        if (!images.TryGetValue(source.Value, out ImageRecord? image))
        {
            throw new ArgumentException("Copy texture does not belong to this Vulkan device.", nameof(source));
        }

        BufferRecord? buffer = buffers.Values.FirstOrDefault(candidate =>
            candidate.AllocationId == destination.AllocationId
            && destination.Offset >= candidate.AllocationOffset
            && destination.Offset - candidate.AllocationOffset < candidate.Description.Size
            && destination.Length <= candidate.Description.Size
                - (destination.Offset - candidate.AllocationOffset));
        if (buffer is null)
        {
            throw new ArgumentException("Destination address is not backed by a live buffer.", nameof(destination));
        }

        ulong bufferOffset = destination.Offset - buffer.AllocationOffset;
        if (bufferOffset > buffer.Description.Size
            || footprint.RequiredBytes > destination.Length
            || footprint.RequiredBytes > buffer.Description.Size - bufferOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(destination));
        }

        if (footprint.Width > image.Description.Width || footprint.Height > image.Description.Height || footprint.BytesPerPixel != 4)
        {
            throw new ArgumentException("Copy footprint is incompatible with the source texture.", nameof(footprint));
        }

        Transition(commandBuffer, image.Image, image.Layout, ImageLayout.TransferSrcOptimal,
            PipelineStageFlags2.ColorAttachmentOutputBit, AccessFlags2.ColorAttachmentWriteBit,
            PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferReadBit);
        images[source.Value] = image with { Layout = ImageLayout.TransferSrcOptimal };

        var region = new BufferImageCopy
        {
            BufferOffset = bufferOffset,
            BufferRowLength = checked((uint)(footprint.RowPitch / footprint.BytesPerPixel)),
            ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new(footprint.Width, footprint.Height, 1),
        };
        vk.CmdCopyImageToBuffer(commandBuffer, image.Image, ImageLayout.TransferSrcOptimal, buffer.Buffer, 1, in region);
    }

    private void RecordUpload(CommandBuffer commandBuffer, GpuMemoryAddress source, GpuTextureHandle destination, GpuTextureCopyFootprint footprint)
    {
        if (!images.TryGetValue(destination.Value, out ImageRecord? image))
        {
            throw new ArgumentException("Destination texture does not belong to this Vulkan device.", nameof(destination));
        }

        BufferRecord? buffer = buffers.Values.FirstOrDefault(candidate =>
            candidate.AllocationId == source.AllocationId
            && source.Offset >= candidate.AllocationOffset
            && source.Offset - candidate.AllocationOffset < candidate.Description.Size
            && source.Length <= candidate.Description.Size
                - (source.Offset - candidate.AllocationOffset));
        if (buffer is null)
        {
            throw new ArgumentException("Source address is not backed by a live buffer.", nameof(source));
        }
        ulong bufferOffset = source.Offset - buffer.AllocationOffset;
        if (bufferOffset > buffer.Description.Size
            || footprint.RequiredBytes > source.Length
            || footprint.RequiredBytes > buffer.Description.Size - bufferOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }
        if (footprint.Width > image.Description.Width || footprint.Height > image.Description.Height || footprint.BytesPerPixel != 4)
        {
            throw new ArgumentException("Copy footprint is incompatible with the destination texture.", nameof(footprint));
        }

        Transition(commandBuffer, image.Image, image.Layout, ImageLayout.TransferDstOptimal,
            PipelineStageFlags2.TopOfPipeBit, 0,
            PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit);
        var region = new BufferImageCopy
        {
            BufferOffset = bufferOffset,
            BufferRowLength = checked((uint)(footprint.RowPitch / footprint.BytesPerPixel)),
            ImageSubresource = new(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new(footprint.Width, footprint.Height, 1),
        };
        vk.CmdCopyBufferToImage(commandBuffer, buffer.Buffer, image.Image, ImageLayout.TransferDstOptimal, 1, in region);
        Transition(commandBuffer, image.Image, ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlags2.AllTransferBit, AccessFlags2.TransferWriteBit,
            PipelineStageFlags2.FragmentShaderBit, AccessFlags2.ShaderReadBit);
        images[destination.Value] = image with { Layout = ImageLayout.ShaderReadOnlyOptimal };
    }

    private void Initialize(uint extensionCount, byte** extensionNames)
    {
        var application = new ApplicationInfo
        {
            SType = StructureType.ApplicationInfo,
            ApiVersion = Vk.Version13,
        };
        var instanceInfo = new InstanceCreateInfo
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &application,
            EnabledExtensionCount = extensionCount,
            PpEnabledExtensionNames = extensionNames,
        };
        Check(vk.CreateInstance(in instanceInfo, null, out instance), "vkCreateInstance");
        uint deviceCount = 0;
        Check(vk.EnumeratePhysicalDevices(instance, ref deviceCount, null), "vkEnumeratePhysicalDevices");
        if (deviceCount == 0)
        {
            throw new VulkanException("No Vulkan physical device is available.");
        }

        PhysicalDevice[] devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* pointer = devices)
        {
            Check(vk.EnumeratePhysicalDevices(instance, ref deviceCount, pointer), "vkEnumeratePhysicalDevices");
        }

        uint queueFamily = 0;
        bool found = false;
        foreach (PhysicalDevice candidate in devices)
        {
            uint count = 0;
            vk.GetPhysicalDeviceQueueFamilyProperties(candidate, ref count, null);
            QueueFamilyProperties[] properties = new QueueFamilyProperties[count];
            fixed (QueueFamilyProperties* pointer = properties)
            {
                vk.GetPhysicalDeviceQueueFamilyProperties(candidate, ref count, pointer);
            }

            for (uint index = 0; index < count; index++)
            {
                const QueueFlags requiredQueueFlags = QueueFlags.GraphicsBit | QueueFlags.ComputeBit;
                if ((properties[index].QueueFlags & requiredQueueFlags) == requiredQueueFlags)
                {
                    physicalDevice = candidate;
                    queueFamily = index;
                    found = true;
                    break;
                }
            }

            if (found)
            {
                break;
            }
        }

        if (!found)
        {
            throw new VulkanException("No Vulkan graphics and compute queue is available.");
        }
        queueFamilyIndex = queueFamily;
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, out memoryProperties);
        vk.GetPhysicalDeviceProperties(physicalDevice, out PhysicalDeviceProperties physicalProperties);
        byte* name = physicalProperties.DeviceName;
        DeviceName = System.Text.Encoding.UTF8.GetString(name, 256).TrimEnd('\0');

        float priority = 1;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = queueFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };
        var dynamicRendering = new PhysicalDeviceDynamicRenderingFeatures
        {
            SType = StructureType.PhysicalDeviceDynamicRenderingFeatures,
            DynamicRendering = true,
        };
        var synchronization2 = new PhysicalDeviceSynchronization2Features
        {
            SType = StructureType.PhysicalDeviceSynchronization2Features,
            Synchronization2 = true,
            PNext = &dynamicRendering,
        };
        var timelineSemaphore = new PhysicalDeviceTimelineSemaphoreFeatures
        {
            SType = StructureType.PhysicalDeviceTimelineSemaphoreFeatures,
            TimelineSemaphore = true,
            PNext = &synchronization2,
        };
        var deviceInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
            PNext = &timelineSemaphore,
        };
        byte[] swapchainExtension = "VK_KHR_swapchain\0"u8.ToArray();
        fixed (byte* swapchainName = swapchainExtension)
        {
            byte** deviceExtensions = stackalloc byte*[1] { swapchainName };
            if (extensionCount != 0)
            {
                deviceInfo.EnabledExtensionCount = 1;
                deviceInfo.PpEnabledExtensionNames = deviceExtensions;
            }
            Check(vk.CreateDevice(physicalDevice, in deviceInfo, null, out device), "vkCreateDevice");
        }
        vk.GetDeviceQueue(device, queueFamily, 0, out queue);
        var textureBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.SampledImage,
            DescriptorCount = MaximumRasterTextureDescriptors,
            StageFlags = ShaderStageFlags.AllGraphics,
        };
        var textureLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &textureBinding,
        };
        Check(vk.CreateDescriptorSetLayout(device, in textureLayoutInfo, null, out textureDescriptorLayout), "vkCreateDescriptorSetLayout");
        var samplerBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.Sampler,
            DescriptorCount = MaximumRasterSamplerDescriptors,
            StageFlags = ShaderStageFlags.AllGraphics,
        };
        var samplerLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &samplerBinding,
        };
        Check(vk.CreateDescriptorSetLayout(device, in samplerLayoutInfo, null, out samplerDescriptorLayout), "vkCreateDescriptorSetLayout");
        var shaderBufferBinding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = MaximumRasterBufferDescriptors,
            StageFlags = ShaderStageFlags.AllGraphics,
        };
        var shaderBufferLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &shaderBufferBinding,
        };
        Check(vk.CreateDescriptorSetLayout(
            device, in shaderBufferLayoutInfo, null, out shaderBufferDescriptorLayout),
            "vkCreateDescriptorSetLayout");
        DescriptorSetLayoutBinding* computeBindings = stackalloc DescriptorSetLayoutBinding[4];
        for (uint binding = 0; binding < 4; binding++)
        {
            computeBindings[binding] = new()
            {
                Binding = binding,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.ComputeBit,
            };
        }
        var computeLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 4,
            PBindings = computeBindings,
        };
        Check(vk.CreateDescriptorSetLayout(
            device, in computeLayoutInfo, null, out computeBufferDescriptorLayout),
            "vkCreateDescriptorSetLayout");
        DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[3]
        {
            new(DescriptorType.SampledImage, 1024),
            new(DescriptorType.Sampler, 1024),
            new(DescriptorType.StorageBuffer, 1024),
        };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
            MaxSets = 1024,
            PoolSizeCount = 3,
            PPoolSizes = poolSizes,
        };
        Check(vk.CreateDescriptorPool(device, in poolInfo, null, out descriptorPool), "vkCreateDescriptorPool");
        var commandPoolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = queueFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };
        Check(vk.CreateCommandPool(device, in commandPoolInfo, null, out commandPool), "vkCreateCommandPool");
        MainQueue = new QueueAdapter(this);
    }

    private Image CreateImage(GpuTextureDescription description, bool aliasable = false)
    {
        var createInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = ToVulkanFormat(description.Format),
            Extent = new(description.Width, description.Height, 1),
            MipLevels = description.MipCount,
            ArrayLayers = description.LayerCount,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ToVulkanUsage(description.Usage),
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
            Flags = aliasable ? ImageCreateFlags.CreateAliasBit : 0,
        };
        Check(vk.CreateImage(device, in createInfo, null, out Image image), "vkCreateImage");
        return image;
    }

    private VkBuffer CreateBuffer(GpuBufferDescription description)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = description.Size,
            Usage = ToVulkanUsage(description.Usage),
            SharingMode = SharingMode.Exclusive,
        };
        Check(vk.CreateBuffer(device, in bufferInfo, null, out VkBuffer buffer), "vkCreateBuffer");
        return buffer;
    }

    private ShaderModule CreateShaderModule(ReadOnlySpan<byte> code)
    {
        if (code.Length % sizeof(uint) != 0)
        {
            throw new ArgumentException("SPIR-V byte length must be a multiple of four.", nameof(code));
        }
        fixed (byte* pointer = code)
        {
            var create = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = checked((nuint)code.Length),
                PCode = (uint*)pointer,
            };
            Check(vk.CreateShaderModule(device, in create, null, out ShaderModule module), "vkCreateShaderModule");
            return module;
        }
    }

    private CommandBuffer BeginCommands()
    {
        var allocationInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Check(vk.AllocateCommandBuffers(device, in allocationInfo, out CommandBuffer commandBuffer), "vkAllocateCommandBuffers");
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Check(vk.BeginCommandBuffer(commandBuffer, in beginInfo), "vkBeginCommandBuffer");
        return commandBuffer;
    }

    private void Transition(
        CommandBuffer commandBuffer,
        Image image,
        ImageLayout oldLayout,
        ImageLayout newLayout,
        PipelineStageFlags2 sourceStage,
        AccessFlags2 sourceAccess,
        PipelineStageFlags2 destinationStage,
        AccessFlags2 destinationAccess,
        ImageAspectFlags aspect = ImageAspectFlags.ColorBit)
    {
        var barrier = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = sourceStage,
            SrcAccessMask = sourceAccess,
            DstStageMask = destinationStage,
            DstAccessMask = destinationAccess,
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = new(aspect, 0, 1, 0, 1),
        };
        var dependency = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &barrier,
        };
        vk.CmdPipelineBarrier2(commandBuffer, in dependency);
    }

    private uint FindMemoryType(uint compatibleTypes, MemoryPropertyFlags required)
    {
        for (uint index = 0; index < memoryProperties.MemoryTypeCount; index++)
        {
            if ((compatibleTypes & (1u << (int)index)) != 0
                && (memoryProperties.MemoryTypes[(int)index].PropertyFlags & required) == required)
            {
                return index;
            }
        }

        throw new VulkanException($"No compatible Vulkan memory type supports {required}.");
    }

    private static Format ToVulkanFormat(GpuFormat format) => format switch
    {
        GpuFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
        GpuFormat.Bgra8Unorm => Format.B8G8R8A8Unorm,
        GpuFormat.Rgba8UnormSrgb => Format.R8G8B8A8Srgb,
        GpuFormat.Bgra8UnormSrgb => Format.B8G8R8A8Srgb,
        GpuFormat.R8Unorm => Format.R8Unorm,
        GpuFormat.Rg8Unorm => Format.R8G8Unorm,
        GpuFormat.R32Float => Format.R32Sfloat,
        GpuFormat.D32Float => Format.D32Sfloat,
        GpuFormat.Depth24PlusStencil8 => Format.D24UnormS8Uint,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static Silk.NET.Vulkan.BlendFactor ToVulkanBlendFactor(GpuBlendFactor factor) => factor switch
    {
        GpuBlendFactor.Zero => Silk.NET.Vulkan.BlendFactor.Zero,
        GpuBlendFactor.One => Silk.NET.Vulkan.BlendFactor.One,
        GpuBlendFactor.SourceColor => Silk.NET.Vulkan.BlendFactor.SrcColor,
        GpuBlendFactor.OneMinusSourceColor => Silk.NET.Vulkan.BlendFactor.OneMinusSrcColor,
        GpuBlendFactor.DestinationColor => Silk.NET.Vulkan.BlendFactor.DstColor,
        GpuBlendFactor.OneMinusDestinationColor => Silk.NET.Vulkan.BlendFactor.OneMinusDstColor,
        GpuBlendFactor.SourceAlpha => Silk.NET.Vulkan.BlendFactor.SrcAlpha,
        GpuBlendFactor.OneMinusSourceAlpha => Silk.NET.Vulkan.BlendFactor.OneMinusSrcAlpha,
        GpuBlendFactor.DestinationAlpha => Silk.NET.Vulkan.BlendFactor.DstAlpha,
        GpuBlendFactor.OneMinusDestinationAlpha => Silk.NET.Vulkan.BlendFactor.OneMinusDstAlpha,
        _ => throw new ArgumentOutOfRangeException(nameof(factor)),
    };

    private static BlendOp ToVulkanBlendOperation(GpuBlendOperation operation) => operation switch
    {
        GpuBlendOperation.Add => BlendOp.Add,
        GpuBlendOperation.Subtract => BlendOp.Subtract,
        GpuBlendOperation.ReverseSubtract => BlendOp.ReverseSubtract,
        GpuBlendOperation.Minimum => BlendOp.Min,
        GpuBlendOperation.Maximum => BlendOp.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static ColorComponentFlags ToVulkanColorWriteMask(GpuColorWriteMask mask)
    {
        ColorComponentFlags result = 0;
        if ((mask & GpuColorWriteMask.Red) != 0) { result |= ColorComponentFlags.RBit; }
        if ((mask & GpuColorWriteMask.Green) != 0) { result |= ColorComponentFlags.GBit; }
        if ((mask & GpuColorWriteMask.Blue) != 0) { result |= ColorComponentFlags.BBit; }
        if ((mask & GpuColorWriteMask.Alpha) != 0) { result |= ColorComponentFlags.ABit; }
        return result;
    }

    private static ImageUsageFlags ToVulkanUsage(GpuTextureUsage usage)
    {
        ImageUsageFlags result = 0;
        if ((usage & GpuTextureUsage.Sampled) != 0)
        { result |= ImageUsageFlags.SampledBit; }
        if ((usage & GpuTextureUsage.Storage) != 0)
        { result |= ImageUsageFlags.StorageBit; }
        if ((usage & GpuTextureUsage.ColorAttachment) != 0)
        { result |= ImageUsageFlags.ColorAttachmentBit; }
        if ((usage & GpuTextureUsage.DepthStencilAttachment) != 0)
        { result |= ImageUsageFlags.DepthStencilAttachmentBit; }
        if ((usage & GpuTextureUsage.CopySource) != 0)
        { result |= ImageUsageFlags.TransferSrcBit; }
        if ((usage & GpuTextureUsage.CopyDestination) != 0)
        { result |= ImageUsageFlags.TransferDstBit; }
        return result;
    }

    private static BufferUsageFlags ToVulkanUsage(GpuBufferUsage usage)
    {
        BufferUsageFlags result = 0;
        if ((usage & GpuBufferUsage.CopySource) != 0)
        { result |= BufferUsageFlags.TransferSrcBit; }
        if ((usage & GpuBufferUsage.CopyDestination) != 0)
        { result |= BufferUsageFlags.TransferDstBit; }
        if ((usage & GpuBufferUsage.ShaderData) != 0)
        { result |= BufferUsageFlags.StorageBufferBit; }
        if ((usage & GpuBufferUsage.IndirectArguments) != 0)
        { result |= BufferUsageFlags.IndirectBufferBit; }
        return result;
    }

    private static Filter ToVulkanFilter(GpuSamplerFilter filter) => filter switch
    {
        GpuSamplerFilter.Nearest => Filter.Nearest,
        GpuSamplerFilter.Linear => Filter.Linear,
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    private static SamplerAddressMode ToVulkanAddressMode(GpuSamplerAddressMode mode) => mode switch
    {
        GpuSamplerAddressMode.ClampToEdge => SamplerAddressMode.ClampToEdge,
        GpuSamplerAddressMode.Repeat => SamplerAddressMode.Repeat,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static ImageAspectFlags Aspect(GpuFormat format) => format switch
    {
        GpuFormat.D32Float => ImageAspectFlags.DepthBit,
        GpuFormat.Depth24PlusStencil8 => ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
        _ => ImageAspectFlags.ColorBit,
    };

    private static PipelineStageFlags2 Stage(GpuStage stage)
    {
        PipelineStageFlags2 nativeStage = 0;
        if (stage == GpuStage.None)
        { nativeStage = PipelineStageFlags2.TopOfPipeBit; }
        if ((stage & GpuStage.ColorOutput) != 0)
        { nativeStage |= PipelineStageFlags2.ColorAttachmentOutputBit; }
        if ((stage & GpuStage.DepthStencil) != 0)
        { nativeStage |= PipelineStageFlags2.EarlyFragmentTestsBit | PipelineStageFlags2.LateFragmentTestsBit; }
        if ((stage & GpuStage.Copy) != 0)
        { nativeStage |= PipelineStageFlags2.AllTransferBit; }
        if ((stage & GpuStage.VertexShader) != 0)
        { nativeStage |= PipelineStageFlags2.VertexShaderBit; }
        if ((stage & GpuStage.PixelShader) != 0)
        { nativeStage |= PipelineStageFlags2.FragmentShaderBit; }
        if ((stage & GpuStage.ComputeShader) != 0)
        { nativeStage |= PipelineStageFlags2.ComputeShaderBit; }
        if ((stage & GpuStage.AllGraphics) != 0)
        { nativeStage |= PipelineStageFlags2.AllGraphicsBit; }
        if ((stage & GpuStage.All) != 0)
        { nativeStage |= PipelineStageFlags2.AllCommandsBit; }
        return nativeStage;
    }

    private static AccessFlags2 AccessForProducer(GpuStage stage)
    {
        AccessFlags2 access = 0;
        if ((stage & GpuStage.ColorOutput) != 0)
        {
            access |= AccessFlags2.ColorAttachmentWriteBit;
        }

        if ((stage & GpuStage.DepthStencil) != 0)
        {
            access |= AccessFlags2.DepthStencilAttachmentWriteBit;
        }

        if ((stage & GpuStage.Copy) != 0)
        {
            access |= AccessFlags2.TransferWriteBit;
        }

        if ((stage & (GpuStage.VertexShader | GpuStage.PixelShader | GpuStage.ComputeShader)) != 0)
        {
            access |= AccessFlags2.ShaderWriteBit;
        }

        return access;
    }

    private static AccessFlags2 AccessForConsumer(GpuStage stage, GpuBarrierHazards hazards)
    {
        AccessFlags2 access = 0;
        if ((stage & GpuStage.ColorOutput) != 0)
        {
            access |= AccessFlags2.ColorAttachmentReadBit | AccessFlags2.ColorAttachmentWriteBit;
        }

        if ((stage & GpuStage.DepthStencil) != 0)
        {
            access |= AccessFlags2.DepthStencilAttachmentReadBit | AccessFlags2.DepthStencilAttachmentWriteBit;
        }

        if ((stage & GpuStage.Copy) != 0)
        {
            access |= AccessFlags2.TransferReadBit | AccessFlags2.TransferWriteBit;
        }

        if ((stage & (GpuStage.VertexShader | GpuStage.PixelShader | GpuStage.ComputeShader)) != 0)
        {
            access |= AccessFlags2.ShaderReadBit | AccessFlags2.ShaderWriteBit;
        }

        if ((hazards & GpuBarrierHazards.IndirectArguments) != 0)
        {
            access |= AccessFlags2.IndirectCommandReadBit;
        }

        return access;
    }

    private static AttachmentLoadOp ToLoadOperation(GpuAttachmentLoadOperation operation) => operation switch
    {
        GpuAttachmentLoadOperation.Load => AttachmentLoadOp.Load,
        GpuAttachmentLoadOperation.Clear => AttachmentLoadOp.Clear,
        GpuAttachmentLoadOperation.Discard => AttachmentLoadOp.DontCare,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new VulkanException($"{operation} failed: {result}.");
        }
    }

    private void VerifyNotDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private ulong NextResourceId() => nextResourceId++;

    private void TrackDescriptorSet(CommandBuffer commandBuffer, DescriptorSet set)
    {
        if (!commandDescriptorSets.TryGetValue(commandBuffer.Handle, out List<DescriptorSet>? sets))
        {
            sets = [];
            commandDescriptorSets.Add(commandBuffer.Handle, sets);
        }
        sets.Add(set);
    }

    private void ReleaseDescriptorSets(CommandBuffer commandBuffer)
    {
        if (!commandDescriptorSets.Remove(commandBuffer.Handle, out List<DescriptorSet>? sets)) { return; }
        foreach (DescriptorSet set in sets)
        {
            Check(vk.FreeDescriptorSets(device, descriptorPool, 1, in set), "vkFreeDescriptorSets");
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (device.Handle != 0)
        {
            vk.DeviceWaitIdle(device);
            foreach (PipelineRecord pipeline in pipelines.Values)
            {
                vk.DestroyPipeline(device, pipeline.Pipeline, null);
                vk.DestroyPipelineLayout(device, pipeline.Layout, null);
            }
            foreach (Sampler sampler in samplers.Values)
            { vk.DestroySampler(device, sampler, null); }
            foreach (BufferRecord buffer in buffers.Values)
            { vk.DestroyBuffer(device, buffer.Buffer, null); }
            foreach (ImageView view in views.Values)
            { vk.DestroyImageView(device, view, null); }
            foreach (ImageRecord image in images.Values)
            {
                if (image.Owned) { vk.DestroyImage(device, image.Image, null); }
            }
            foreach (MemoryRecord memory in memories.Values)
            {
                if (memory.CpuAddress != 0) { vk.UnmapMemory(device, memory.Memory); }
                vk.FreeMemory(device, memory.Memory, null);
            }
            if (descriptorPool.Handle != 0)
            { vk.DestroyDescriptorPool(device, descriptorPool, null); }
            if (textureDescriptorLayout.Handle != 0)
            { vk.DestroyDescriptorSetLayout(device, textureDescriptorLayout, null); }
            if (samplerDescriptorLayout.Handle != 0)
            { vk.DestroyDescriptorSetLayout(device, samplerDescriptorLayout, null); }
            if (shaderBufferDescriptorLayout.Handle != 0)
            { vk.DestroyDescriptorSetLayout(device, shaderBufferDescriptorLayout, null); }
            if (computeBufferDescriptorLayout.Handle != 0)
            { vk.DestroyDescriptorSetLayout(device, computeBufferDescriptorLayout, null); }
            if (commandPool.Handle != 0)
            { vk.DestroyCommandPool(device, commandPool, null); }
            vk.DestroyDevice(device, null);
        }
        if (instance.Handle != 0)
        { vk.DestroyInstance(instance, null); }
        vk.Dispose();
    }

    private sealed record ImageRecord(
        Image Image,
        GpuTextureDescription Description,
        ImageLayout Layout,
        bool Owned,
        ulong AllocationId = 0);

    private sealed record BufferRecord(
        VkBuffer Buffer,
        GpuBufferDescription Description,
        ulong AllocationId,
        ulong AllocationOffset);

    private sealed class MemoryRecord(
        DeviceMemory memory,
        ulong size,
        GpuMemoryKind kind,
        nint cpuAddress,
        uint memoryType)
    {
        public DeviceMemory Memory { get; } = memory;
        public ulong Size { get; } = size;
        public GpuMemoryKind Kind { get; } = kind;
        public nint CpuAddress { get; } = cpuAddress;
        public uint MemoryType { get; } = memoryType;
        public int BoundResourceCount { get; set; }

        public bool MatchesRoot(GpuMemoryAllocation allocation) =>
            allocation.Size == Size && allocation.Kind == Kind && allocation.CpuAddress == CpuAddress
            && allocation.MemoryAddress.Offset == 0 && allocation.MemoryAddress.Length == Size;

        public bool MatchesRegion(GpuMemoryAllocation allocation) =>
            allocation.Kind == Kind
            && allocation.MemoryAddress.Offset <= Size
            && allocation.Size <= Size - allocation.MemoryAddress.Offset
            && allocation.MemoryAddress.Length >= allocation.Size
            && allocation.CpuAddress == (CpuAddress == 0
                ? 0
                : checked(CpuAddress + (nint)allocation.MemoryAddress.Offset));
    }
    private sealed record PipelineRecord(VkPipeline Pipeline, PipelineLayout Layout);
    private sealed class QueueAdapter(VulkanDevice owner) : IGpuQueue
    {
        public GpuCommandBuffer StartCommandRecording()
            => GpuBackendCommands.CreateCommandBuffer(new VulkanRecorder(owner, owner.BeginCommands()));

        public GpuSemaphore CreateSemaphore(ulong initialValue = 0) => new VulkanSemaphore(owner, initialValue);

        public void Submit(ReadOnlySpan<GpuCommandBuffer> commandBuffers, GpuSemaphore signalSemaphore, ulong signalValue)
        {
            if (signalSemaphore is not VulkanSemaphore native || native.Owner != owner)
            {
                throw new ArgumentException("Semaphore belongs to another backend.", nameof(signalSemaphore));
            }

            owner.Submit(commandBuffers, native, signalValue);
        }

        public void Wait(GpuSemaphore semaphore, ulong value)
        {
            if (semaphore is not VulkanSemaphore native || native.Owner != owner)
            {
                throw new ArgumentException("Semaphore belongs to another backend.", nameof(semaphore));
            }

            owner.Wait(native, value);
        }

        public bool IsComplete(GpuSemaphore semaphore, ulong value)
        {
            if (semaphore is not VulkanSemaphore native || native.Owner != owner)
            {
                throw new ArgumentException("Semaphore belongs to another backend.", nameof(semaphore));
            }

            return owner.IsComplete(native, value);
        }
    }

    private sealed class VulkanRecorder(VulkanDevice owner, CommandBuffer commandBuffer) : IGpuCommandRecorder
    {
        private PipelineRecord? currentPipeline;
        private PipelineRecord? currentComputePipeline;
        private DescriptorSet computeDescriptorSet;
        public VulkanDevice Owner { get; } = owner;
        public CommandBuffer CommandBuffer { get; } = commandBuffer;
        public void Barrier(GpuStage before, GpuStage after, GpuBarrierHazards hazards) => Owner.RecordBarrier(CommandBuffer, before, after, hazards);
        public void AliasingBarrier(
            GpuAliasingResource beforeResource,
            GpuAliasingResource afterResource,
            GpuStage before,
            GpuStage after,
            GpuBarrierHazards hazards) => Owner.RecordBarrier(CommandBuffer, before, after, hazards);
        public void BeginRendering(IReadOnlyList<GpuColorAttachment> colors, GpuDepthStencilAttachment? depth) => Owner.RecordBeginRendering(CommandBuffer, colors, depth);
        public void EndRendering() => Owner.vk.CmdEndRendering(CommandBuffer);
        public void SetPipeline(GpuRasterPipelineHandle pipeline)
        {
            if (!Owner.pipelines.TryGetValue(pipeline.Value, out PipelineRecord? record))
            {
                throw new ArgumentException("Pipeline does not belong to this Vulkan device.", nameof(pipeline));
            }
            Owner.vk.CmdBindPipeline(CommandBuffer, PipelineBindPoint.Graphics, record.Pipeline);
            currentPipeline = record;
        }
        public void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor)
        {
            var nativeViewport = new Viewport(viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
            var nativeScissor = new Rect2D(new(checked((int)scissor.X), checked((int)scissor.Y)), new(scissor.Width, scissor.Height));
            Owner.vk.CmdSetViewport(CommandBuffer, 0, 1, in nativeViewport);
            Owner.vk.CmdSetScissor(CommandBuffer, 0, 1, in nativeScissor);
        }
        public void Draw(uint vertexCount, uint instanceCount) => Owner.vk.CmdDraw(CommandBuffer, vertexCount, instanceCount, 0, 0);
        public void CopyMemoryToTexture(GpuMemoryAddress source, GpuTextureHandle destination, GpuTextureCopyFootprint footprint) => Owner.RecordUpload(CommandBuffer, source, destination, footprint);
        public void CopyTextureToMemory(GpuTextureHandle source, GpuMemoryAddress destination, GpuTextureCopyFootprint footprint) => Owner.RecordCopy(CommandBuffer, source, destination, footprint);
        public void SetResourceTable(GpuResourceTable table)
        {
            if (currentPipeline is null)
            {
                throw new InvalidOperationException("A raster pipeline must be bound before a resource table.");
            }
            ArgumentNullException.ThrowIfNull(table);
            if (table.TextureSlotCount > MaximumRasterTextureDescriptors
                || table.SamplerSlotCount > MaximumRasterSamplerDescriptors
                || table.BufferSlotCount > MaximumRasterBufferDescriptors)
            {
                throw new NotSupportedException(
                    "The current Vulkan descriptor sets support at most 64 indices per resource kind.");
            }

            if (table.TextureSlotCount != 0)
            {
                DescriptorSet textureSet = Allocate(Owner.textureDescriptorLayout);
                for (int slot = 0; slot < table.TextureSlotCount; slot++)
                {
                    TextureId id = table.GetTexture(slot);
                    if (id.IsNull) { continue; }
                    if (!Owner.views.TryGetValue(id.Value, out ImageView view))
                    {
                        throw new ArgumentException($"Texture slot {slot} does not belong to this Vulkan device.", nameof(table));
                    }
                    var imageInfo = new DescriptorImageInfo(default, view, ImageLayout.ShaderReadOnlyOptimal);
                    var write = new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = textureSet,
                        DstBinding = 0,
                        DstArrayElement = checked((uint)slot),
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.SampledImage,
                        PImageInfo = &imageInfo,
                    };
                    Owner.vk.UpdateDescriptorSets(Owner.device, 1, in write, 0, null);
                }
                Owner.vk.CmdBindDescriptorSets(
                    CommandBuffer, PipelineBindPoint.Graphics, currentPipeline.Layout, 0, 1, in textureSet, 0, null);
            }

            if (table.SamplerSlotCount != 0)
            {
                DescriptorSet samplerSet = Allocate(Owner.samplerDescriptorLayout);
                for (int slot = 0; slot < table.SamplerSlotCount; slot++)
                {
                    SamplerId id = table.GetSampler(slot);
                    if (id.IsNull) { continue; }
                    if (!Owner.samplers.TryGetValue(id.Value, out Sampler sampler))
                    {
                        throw new ArgumentException($"Sampler slot {slot} does not belong to this Vulkan device.", nameof(table));
                    }
                    var imageInfo = new DescriptorImageInfo(sampler, default, ImageLayout.Undefined);
                    var write = new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = samplerSet,
                        DstBinding = 0,
                        DstArrayElement = checked((uint)slot),
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.Sampler,
                        PImageInfo = &imageInfo,
                    };
                    Owner.vk.UpdateDescriptorSets(Owner.device, 1, in write, 0, null);
                }
                Owner.vk.CmdBindDescriptorSets(
                    CommandBuffer, PipelineBindPoint.Graphics, currentPipeline.Layout, 1, 1, in samplerSet, 0, null);
            }

            if (table.BufferSlotCount != 0)
            {
                DescriptorSet bufferSet = Allocate(Owner.shaderBufferDescriptorLayout);
                for (int slot = 0; slot < table.BufferSlotCount; slot++)
                {
                    BufferId id = table.GetBuffer(slot);
                    if (id.IsNull) { continue; }
                    if (!Owner.bufferViews.TryGetValue(id.Value, out GpuBufferView view)
                        || !Owner.buffers.TryGetValue(view.Buffer.Value, out BufferRecord? buffer))
                    {
                        throw new ArgumentException(
                            $"Buffer index {slot} does not belong to this Vulkan device.",
                            nameof(table));
                    }
                    if ((buffer.Description.Usage & GpuBufferUsage.ShaderData) == 0)
                    {
                        throw new ArgumentException(
                            $"Buffer index {slot} requires ShaderData usage.",
                            nameof(table));
                    }
                    var bufferInfo = new DescriptorBufferInfo(
                        buffer.Buffer,
                        view.Description.Offset,
                        view.Description.Length);
                    var write = new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = bufferSet,
                        DstBinding = 0,
                        DstArrayElement = checked((uint)slot),
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.StorageBuffer,
                        PBufferInfo = &bufferInfo,
                    };
                    Owner.vk.UpdateDescriptorSets(Owner.device, 1, in write, 0, null);
                }
                Owner.vk.CmdBindDescriptorSets(
                    CommandBuffer, PipelineBindPoint.Graphics, currentPipeline.Layout, 2, 1, in bufferSet, 0, null);
            }
        }
        public void SetRootData(ReadOnlySpan<byte> data)
        {
            if (currentPipeline is null)
            {
                throw new InvalidOperationException("A raster pipeline must be bound before root data.");
            }
            fixed (byte* pointer = data)
            {
                Owner.vk.CmdPushConstants(CommandBuffer, currentPipeline.Layout, ShaderStageFlags.AllGraphics, 0, checked((uint)data.Length), pointer);
            }
        }
        public void SetComputePipeline(GpuComputePipelineHandle pipeline)
        {
            if (!Owner.pipelines.TryGetValue(pipeline.Value, out PipelineRecord? record))
            {
                throw new ArgumentException("Pipeline does not belong to this Vulkan device.", nameof(pipeline));
            }
            Owner.vk.CmdBindPipeline(CommandBuffer, PipelineBindPoint.Compute, record.Pipeline);
            currentComputePipeline = record;
            computeDescriptorSet = default;
        }
        public void SetComputeBuffer(uint slot, GpuBufferHandle buffer)
        {
            if (currentComputePipeline is null)
            {
                throw new InvalidOperationException("A compute pipeline must be bound before a compute buffer.");
            }
            if (slot >= 4)
            {
                throw new ArgumentOutOfRangeException(nameof(slot), "Vulkan compute pipelines support buffer slots 0 through 3.");
            }
            if (!Owner.buffers.TryGetValue(buffer.Value, out BufferRecord? record))
            {
                throw new ArgumentException("Buffer does not belong to this Vulkan device.", nameof(buffer));
            }
            if ((record.Description.Usage & GpuBufferUsage.ShaderData) == 0)
            {
                throw new ArgumentException("Compute buffers require ShaderData usage.", nameof(buffer));
            }
            if (computeDescriptorSet.Handle == 0)
            {
                computeDescriptorSet = Allocate(Owner.computeBufferDescriptorLayout);
            }

            var bufferInfo = new DescriptorBufferInfo(record.Buffer, 0, record.Description.Size);
            var write = new WriteDescriptorSet
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = computeDescriptorSet,
                DstBinding = slot,
                DescriptorCount = 1,
                DescriptorType = DescriptorType.StorageBuffer,
                PBufferInfo = &bufferInfo,
            };
            Owner.vk.UpdateDescriptorSets(Owner.device, 1, in write, 0, null);
            DescriptorSet set = computeDescriptorSet;
            Owner.vk.CmdBindDescriptorSets(
                CommandBuffer, PipelineBindPoint.Compute, currentComputePipeline.Layout,
                0, 1, in set, 0, null);
        }
        public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            if (currentComputePipeline is null)
            {
                throw new InvalidOperationException("A compute pipeline must be bound before dispatch.");
            }
            if (computeDescriptorSet.Handle == 0)
            {
                throw new InvalidOperationException("A compute buffer must be bound before dispatch.");
            }
            Owner.vk.CmdDispatch(CommandBuffer, groupCountX, groupCountY, groupCountZ);
        }
        public void End() => Check(Owner.vk.EndCommandBuffer(CommandBuffer), "vkEndCommandBuffer");

        private DescriptorSet Allocate(DescriptorSetLayout layout)
        {
            var allocate = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = Owner.descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = &layout,
            };
            Check(Owner.vk.AllocateDescriptorSets(Owner.device, in allocate, out DescriptorSet set), "vkAllocateDescriptorSets");
            Owner.TrackDescriptorSet(CommandBuffer, set);
            return set;
        }
    }

    internal sealed class VulkanSemaphore : GpuSemaphore
    {
        private readonly SortedDictionary<ulong, List<CommandBuffer>> pending = [];
        private ulong lastSignalValue;
        private bool disposed;
        public VulkanDevice Owner { get; }
        public VkSemaphore Handle { get; }

        public VulkanSemaphore(VulkanDevice owner, ulong initialValue)
        {
            Owner = owner;
            lastSignalValue = initialValue;
            var type = new SemaphoreTypeCreateInfo
            {
                SType = StructureType.SemaphoreTypeCreateInfo,
                SemaphoreType = SemaphoreType.Timeline,
                InitialValue = initialValue,
            };
            var create = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo, PNext = &type };
            Check(owner.vk.CreateSemaphore(owner.device, in create, null, out VkSemaphore handle), "vkCreateSemaphore");
            Handle = handle;
        }

        public void Track(ulong value, CommandBuffer[] buffers)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(VulkanSemaphore));
            }

            lastSignalValue = value;
            pending.Add(value, [.. buffers]);
        }

        public void ValidateSignal(ulong value)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(VulkanSemaphore));
            }
            if (value <= lastSignalValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Signal values must increase monotonically.");
            }
        }

        public void ValidateWait(ulong value)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(VulkanSemaphore));
            }
            if (value > lastSignalValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Cannot wait for an unsignaled value.");
            }
        }

        public void ReleaseCompleted(ulong value)
        {
            foreach (ulong key in pending.Keys.TakeWhile(key => key <= value).ToArray())
            {
                foreach (CommandBuffer commandBuffer in pending[key])
                {
                    Owner.ReleaseDescriptorSets(commandBuffer);
                    Owner.vk.FreeCommandBuffers(Owner.device, Owner.commandPool, 1, in commandBuffer);
                }

                pending.Remove(key);
            }
        }

        public override void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (pending.Count != 0)
            {
                throw new InvalidOperationException("Semaphore still owns in-flight command buffers.");
            }

            disposed = true;
            Owner.vk.DestroySemaphore(Owner.device, Handle, null);
        }
    }
}

public sealed class VulkanException(string message) : Exception(message);
