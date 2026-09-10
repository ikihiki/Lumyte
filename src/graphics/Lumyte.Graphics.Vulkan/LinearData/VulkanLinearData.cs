using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    public NativeGpuMemoryRequirements GetLinearMemoryRequirements(ulong size, NativeGpuMemoryKind kind)
    {
        VerifyNotDisposed();
        VkBuffer buffer = CreateLinearBuffer(size, kind);
        try
        {
            vk.GetBufferMemoryRequirements(device, buffer, out MemoryRequirements requirements);
            var reservation = ReserveMemory(requirements.Size, requirements.Alignment, bufferImageGranularity);
            return new(reservation.Size, reservation.Alignment, new MemoryCompatibility(this, kind, requirements.MemoryTypeBits));
        }
        finally { vk.DestroyBuffer(device, buffer, null); }
    }

    public NativeGpuLinearRegion CreateLinearRegion(ulong size, NativeGpuHeap heap, ulong offset)
    {
        VerifyNotDisposed();
        HeapRecord allocation = RequireHeap(heap);
        VkBuffer buffer = CreateLinearBuffer(size, heap.Kind);
        bool acquiredMapping = false;
        try
        {
            Check(vk.BindBufferMemory(device, buffer, allocation.Memory, offset), "vkBindBufferMemory");
            BufferDeviceAddressInfo addressInfo = new() { SType = StructureType.BufferDeviceAddressInfo, Buffer = buffer };
            ulong gpuAddress = vk.GetBufferDeviceAddress(device, in addressInfo);
            nint cpuAddress = 0;
            if (heap.Kind != NativeGpuMemoryKind.GpuOnly)
            {
                // Pointer arithmetic is host memory safety; native placement validation remains native.
                nint hostOffset = checked((nint)offset);
                _ = checked((nint)size);
                if (offset > heap.Size || size > heap.Size - offset)
                {
                    throw new ArgumentOutOfRangeException(nameof(size), "The mapped region must fit the host allocation.");
                }
                if (allocation.MappedRegionCount == 0)
                {
                    void* mapped = null;
                    Check(vk.MapMemory(device, allocation.Memory, 0, Vk.WholeSize, 0, &mapped), "vkMapMemory");
                    allocation.MappedAddress = (nint)mapped;
                }
                allocation.MappedRegionCount++;
                acquiredMapping = true;
                cpuAddress = checked(allocation.MappedAddress + hostOffset);
            }
            return new LinearRecord(this, buffer, allocation, acquiredMapping, offset, size, gpuAddress, cpuAddress);
        }
        catch
        {
            if (acquiredMapping) { ReleaseMapping(allocation); }
            vk.DestroyBuffer(device, buffer, null);
            throw;
        }
    }

    public void DestroyLinearRegion(NativeGpuLinearRegion region)
    {
        VerifyNotDisposed();
        ArgumentNullException.ThrowIfNull(region);
        if (region is not LinearRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("Linear region belongs to another device.", nameof(region));
        }
        ObjectDisposedException.ThrowIf(record.Destroyed, region);
        if (record.HasMapping) { ReleaseMapping(record.Allocation); }
        vk.DestroyBuffer(device, record.Buffer, null);
        record.Destroyed = true;
    }

    private VkBuffer CreateLinearBuffer(ulong size, NativeGpuMemoryKind kind)
    {
        if (kind is not (NativeGpuMemoryKind.CpuVisible or NativeGpuMemoryKind.GpuOnly or NativeGpuMemoryKind.Readback))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        BufferCreateInfo description = new()
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            SharingMode = SharingMode.Exclusive,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit
                | BufferUsageFlags.StorageBufferBit | BufferUsageFlags.UniformBufferBit
                | BufferUsageFlags.IndexBufferBit | BufferUsageFlags.IndirectBufferBit
                | BufferUsageFlags.ShaderDeviceAddressBit,
        };
        Check(vk.CreateBuffer(device, in description, null, out VkBuffer buffer), "vkCreateBuffer");
        return buffer;
    }

    private void ReleaseMapping(HeapRecord allocation)
    {
        allocation.MappedRegionCount--;
        if (allocation.MappedRegionCount == 0)
        {
            vk.UnmapMemory(device, allocation.Memory);
            allocation.MappedAddress = 0;
        }
    }

    private sealed class LinearRecord(VulkanBackend owner, VkBuffer buffer, HeapRecord allocation,
        bool hasMapping, ulong heapOffset, ulong size, ulong gpuAddress, nint cpuAddress)
        : NativeGpuLinearRegion(allocation, heapOffset, size, gpuAddress, cpuAddress)
    {
        public VulkanBackend Owner { get; } = owner;
        public VkBuffer Buffer { get; } = buffer;
        public HeapRecord Allocation { get; } = allocation;
        public bool HasMapping { get; } = hasMapping;
        public bool Destroyed;
    }
}
