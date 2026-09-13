using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    private NativeDescriptorHeapProperties descriptorProperties;
    private delegate* unmanaged<Device, uint, NativeResourceDescriptorInfo*, NativeHostAddressRange*, Result> writeResourceDescriptors;
    private delegate* unmanaged<Device, uint, SamplerCreateInfo*, NativeHostAddressRange*, Result> writeSamplerDescriptors;
    private delegate* unmanaged<CommandBuffer, NativeBindHeapInfo*, void> bindResourceHeap;
    private delegate* unmanaged<CommandBuffer, NativeBindHeapInfo*, void> bindSamplerHeap;

    private void InitializeDescriptors(PhysicalDevice physicalDevice)
    {
        NativeDescriptorHeapProperties properties = new() { SType = (StructureType)1000135008 };
        PhysicalDeviceProperties2 query = new() { SType = StructureType.PhysicalDeviceProperties2, PNext = &properties };
        vk.GetPhysicalDeviceProperties2(physicalDevice, &query);
        descriptorProperties = properties;
        writeResourceDescriptors = (delegate* unmanaged<Device, uint, NativeResourceDescriptorInfo*, NativeHostAddressRange*, Result>)
            (nint)vk.GetDeviceProcAddr(device, "vkWriteResourceDescriptorsEXT");
        writeSamplerDescriptors = (delegate* unmanaged<Device, uint, SamplerCreateInfo*, NativeHostAddressRange*, Result>)
            (nint)vk.GetDeviceProcAddr(device, "vkWriteSamplerDescriptorsEXT");
        bindResourceHeap = (delegate* unmanaged<CommandBuffer, NativeBindHeapInfo*, void>)
            (nint)vk.GetDeviceProcAddr(device, "vkCmdBindResourceHeapEXT");
        bindSamplerHeap = (delegate* unmanaged<CommandBuffer, NativeBindHeapInfo*, void>)
            (nint)vk.GetDeviceProcAddr(device, "vkCmdBindSamplerHeapEXT");
        if (writeResourceDescriptors == null || writeSamplerDescriptors == null || bindResourceHeap == null || bindSamplerHeap == null)
        {
            throw new NotSupportedException("Required VK_EXT_descriptor_heap entry points are unavailable.");
        }
    }

    public NativeGpuDescriptorHeap CreateDescriptorHeap(NativeGpuDescriptorHeapKind kind, uint capacity)
    {
        VerifyNotDisposed();
        DescriptorStorageLayout layout = DescriptorLayout(descriptorProperties, kind, capacity);
        _ = checked((nint)layout.BackingSize);
        BufferCreateInfo info = new()
        {
            SType = StructureType.BufferCreateInfo, Size = layout.BackingSize, SharingMode = SharingMode.Exclusive,
            Usage = BufferUsageFlags.ShaderDeviceAddressBit | (BufferUsageFlags)0x10000000,
        };
        CheckDeviceResult(vk.CreateBuffer(device, in info, null, out VkBuffer buffer), "vkCreateBuffer(descriptor heap)");
        DeviceMemory memory = default;
        bool mapped = false;
        try
        {
            vk.GetBufferMemoryRequirements(device, buffer, out MemoryRequirements requirements);
            Span<MemoryPropertyFlags> properties = stackalloc MemoryPropertyFlags[checked((int)memoryProperties.MemoryTypeCount)];
            for (int index = 0; index < properties.Length; index++) { properties[index] = memoryProperties.MemoryTypes[index].PropertyFlags; }
            uint memoryType = SelectMemoryType(requirements.MemoryTypeBits, properties, NativeGpuMemoryKind.CpuVisible);
            MemoryAllocateFlagsInfo flags = new() { SType = StructureType.MemoryAllocateFlagsInfo, Flags = MemoryAllocateFlags.DeviceAddressBit };
            MemoryAllocateInfo allocation = new()
            {
                SType = StructureType.MemoryAllocateInfo, PNext = &flags,
                AllocationSize = requirements.Size, MemoryTypeIndex = memoryType,
            };
            CheckDeviceResult(vk.AllocateMemory(device, in allocation, null, out memory), "vkAllocateMemory(descriptor heap)");
            CheckDeviceResult(vk.BindBufferMemory(device, buffer, memory, 0), "vkBindBufferMemory(descriptor heap)");
            void* address = null;
            CheckDeviceResult(vk.MapMemory(device, memory, 0, Vk.WholeSize, 0, &address), "vkMapMemory(descriptor heap)");
            mapped = true;
            BufferDeviceAddressInfo addressInfo = new() { SType = StructureType.BufferDeviceAddressInfo, Buffer = buffer };
            ulong baseAddress = vk.GetBufferDeviceAddress(device, in addressInfo);
            ulong alignedAddress = AlignDescriptorBytes(baseAddress, layout.HeapAlignment);
            ulong offset = alignedAddress - baseAddress;
            nint hostAddress = checked((nint)address + checked((nint)offset));
            NativeBindHeapInfo bindInfo = new()
            {
                SType = (StructureType)1000135003,
                HeapRange = new() { Address = alignedAddress, Size = layout.BindSize },
                ReservedRangeOffset = layout.ReservedOffset, ReservedRangeSize = layout.ReservedSize,
            };
            return new DescriptorHeapRecord(this, kind, capacity, buffer, memory, hostAddress, layout.Stride, bindInfo);
        }
        catch
        {
            if (mapped) { vk.UnmapMemory(device, memory); }
            vk.DestroyBuffer(device, buffer, null);
            if (memory.Handle != 0) { vk.FreeMemory(device, memory, null); }
            throw;
        }
    }

    public void DestroyDescriptorHeap(NativeGpuDescriptorHeap heap)
    {
        VerifyNotDisposed();
        DescriptorHeapRecord record = RequireDescriptorHeap(heap);
        vk.UnmapMemory(device, record.Memory);
        vk.DestroyBuffer(device, record.Buffer, null);
        vk.FreeMemory(device, record.Memory, null);
        record.Destroyed = true;
    }

    public void WriteTextureDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuTextureView view,
        NativeGpuTextureDescriptorType type = NativeGpuTextureDescriptorType.Sampled)
    {
        VerifyNotDisposed();
        DescriptorHeapRecord record = RequireDescriptorHeap(heap, NativeGpuDescriptorHeapKind.Resource);
        NativeHostAddressRange destination = record.Slot(index, descriptorProperties.ImageDescriptorSize);
        TextureRecord texture = RequireCommandTexture(view.Texture);
        DescriptorType nativeType = TextureDescriptorType(type);
        ImageViewUsageCreateInfo usage = new()
        {
            SType = StructureType.ImageViewUsageCreateInfo,
            Usage = type == NativeGpuTextureDescriptorType.Sampled ? ImageUsageFlags.SampledBit : ImageUsageFlags.StorageBit,
        };
        ImageViewCreateInfo imageView = TextureViewDescription(view, texture.Image);
        imageView.PNext = &usage;
        NativeImageDescriptorInfo image = new() { SType = (StructureType)1000135001, View = &imageView, Layout = ImageLayout.General };
        NativeResourceDescriptorInfo descriptor = new() { SType = (StructureType)1000135002, Type = nativeType, Data = &image };
        CheckDeviceResult(writeResourceDescriptors(device, 1, &descriptor, &destination), "vkWriteResourceDescriptorsEXT(image)");
    }

    public void WriteBufferDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuRange range, NativeGpuBufferAccess access)
    {
        VerifyNotDisposed();
        DescriptorHeapRecord record = RequireDescriptorHeap(heap, NativeGpuDescriptorHeapKind.Resource);
        NativeHostAddressRange destination = record.Slot(index, descriptorProperties.BufferDescriptorSize);
        RequireCommandRange(range);
        NativeDeviceAddressRange address = new() { Address = range.GpuAddress, Size = range.Size };
        NativeResourceDescriptorInfo descriptor = new()
        {
            SType = (StructureType)1000135002, Type = BufferDescriptorType(access), Data = &address,
        };
        CheckDeviceResult(writeResourceDescriptors(device, 1, &descriptor, &destination), "vkWriteResourceDescriptorsEXT(buffer)");
    }

    public void WriteSamplerDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuSamplerDescription description)
    {
        VerifyNotDisposed();
        DescriptorHeapRecord record = RequireDescriptorHeap(heap, NativeGpuDescriptorHeapKind.Sampler);
        NativeHostAddressRange destination = record.Slot(index, descriptorProperties.SamplerDescriptorSize);
        SamplerCreateInfo info = SamplerDescription(description);
        CheckDeviceResult(writeSamplerDescriptors(device, 1, &info, &destination), "vkWriteSamplerDescriptorsEXT");
    }

    private DescriptorHeapRecord RequireDescriptorHeap(NativeGpuDescriptorHeap heap, NativeGpuDescriptorHeapKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(heap);
        if (heap is not DescriptorHeapRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("Descriptor heap belongs to another backend.", nameof(heap));
        }
        ObjectDisposedException.ThrowIf(record.Destroyed, heap);
        if (kind.HasValue && heap.Kind != kind.Value) { throw new ArgumentException("Descriptor heap has the wrong kind.", nameof(heap)); }
        return record;
    }

    private sealed class DescriptorHeapRecord(VulkanBackend owner, NativeGpuDescriptorHeapKind kind, uint capacity,
        VkBuffer buffer, DeviceMemory memory, nint cpuAddress, ulong stride, NativeBindHeapInfo bindInfo)
        : NativeGpuDescriptorHeap(kind, capacity)
    {
        public VulkanBackend Owner { get; } = owner;
        public VkBuffer Buffer { get; } = buffer;
        public DeviceMemory Memory { get; } = memory;
        public NativeBindHeapInfo BindInfo { get; } = bindInfo;
        public bool Destroyed;

        public NativeHostAddressRange Slot(uint index, ulong size)
        {
            // The native function receives only a host address, so it cannot validate our slot bound.
            if (index >= Capacity) { throw new ArgumentOutOfRangeException(nameof(index)); }
            return new()
            {
                Address = (void*)checked(cpuAddress + checked((nint)checked(index * stride))),
                Size = checked((nuint)size),
            };
        }
    }
}
