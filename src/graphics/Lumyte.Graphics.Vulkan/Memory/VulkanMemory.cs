using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

public sealed unsafe partial class VulkanBackend
{
    public NativeGpuHeap CreateGpuHeap(ulong size, ulong alignment, NativeGpuMemoryKind kind,
        ReadOnlySpan<NativeGpuMemoryCompatibility> compatibilities)
    {
        VerifyNotDisposed();
        if (compatibilities.IsEmpty)
        {
            throw new ArgumentException("At least one memory compatibility requirement is required.", nameof(compatibilities));
        }
        uint compatibleTypes = uint.MaxValue;
        foreach (NativeGpuMemoryCompatibility compatibility in compatibilities)
        {
            if (compatibility is not MemoryCompatibility native || !ReferenceEquals(native.Owner, this) || native.Kind != kind)
            {
                throw new ArgumentException("Memory requirements must belong to this device and memory kind.", nameof(compatibilities));
            }
            compatibleTypes &= native.MemoryTypeBits;
        }
        Span<MemoryPropertyFlags> properties = stackalloc MemoryPropertyFlags[checked((int)memoryProperties.MemoryTypeCount)];
        for (int index = 0; index < properties.Length; index++) { properties[index] = memoryProperties.MemoryTypes[index].PropertyFlags; }
        uint memoryType = SelectMemoryType(compatibleTypes, properties, kind);
        MemoryAllocateFlagsInfo addressFlags = new()
        {
            SType = StructureType.MemoryAllocateFlagsInfo,
            Flags = MemoryAllocateFlags.DeviceAddressBit,
        };
        MemoryAllocateInfo info = new()
        {
            SType = StructureType.MemoryAllocateInfo, PNext = &addressFlags,
            AllocationSize = size, MemoryTypeIndex = memoryType,
        };
        Check(vk.AllocateMemory(device, in info, null, out DeviceMemory memory), "vkAllocateMemory");
        try { return new HeapRecord(this, memory, size, alignment, kind); }
        catch
        {
            vk.FreeMemory(device, memory, null);
            throw;
        }
    }

    public void DestroyGpuHeap(NativeGpuHeap heap)
    {
        VerifyNotDisposed();
        HeapRecord record = RequireHeap(heap);
        vk.FreeMemory(device, record.Memory, null);
        record.Destroyed = true;
    }

    private HeapRecord RequireHeap(NativeGpuHeap heap)
    {
        ArgumentNullException.ThrowIfNull(heap);
        if (heap is not HeapRecord record || !ReferenceEquals(record.Owner, this))
        {
            throw new ArgumentException("Heap belongs to another device.", nameof(heap));
        }
        ObjectDisposedException.ThrowIf(record.Destroyed, heap);
        return record;
    }

    internal static (ulong Size, ulong Alignment) ReserveMemory(ulong size, ulong alignment, ulong granularity)
    {
        ulong effectiveAlignment = Math.Max(alignment, granularity);
        ulong alignedSize = checked((size + effectiveAlignment - 1) / effectiveAlignment * effectiveAlignment);
        return (alignedSize, effectiveAlignment);
    }

    internal static uint SelectMemoryType(uint compatibleTypes, ReadOnlySpan<MemoryPropertyFlags> properties, NativeGpuMemoryKind kind)
    {
        MemoryPropertyFlags required = kind switch
        {
            NativeGpuMemoryKind.GpuOnly => MemoryPropertyFlags.DeviceLocalBit,
            NativeGpuMemoryKind.CpuVisible or NativeGpuMemoryKind.Readback => MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        uint? firstCompatible = null;
        for (int index = 0; index < properties.Length; index++)
        {
            if ((compatibleTypes & (1u << index)) == 0 || (properties[index] & required) != required) { continue; }
            firstCompatible ??= (uint)index;
            if (kind != NativeGpuMemoryKind.Readback || (properties[index] & MemoryPropertyFlags.HostCachedBit) != 0)
            {
                return (uint)index;
            }
        }
        return firstCompatible ?? throw new NotSupportedException("No Vulkan memory type satisfies all requirements and the requested memory kind.");
    }

    private sealed class MemoryCompatibility(VulkanBackend owner, NativeGpuMemoryKind kind, uint memoryTypeBits)
        : NativeGpuMemoryCompatibility
    {
        public VulkanBackend Owner { get; } = owner;
        public NativeGpuMemoryKind Kind { get; } = kind;
        public uint MemoryTypeBits { get; } = memoryTypeBits;
    }

    private sealed class HeapRecord(VulkanBackend owner, DeviceMemory memory, ulong size, ulong alignment, NativeGpuMemoryKind kind)
        : NativeGpuHeap(size, alignment, kind)
    {
        public VulkanBackend Owner { get; } = owner;
        public DeviceMemory Memory { get; } = memory;
        public nint MappedAddress;
        public int MappedRegionCount;
        public bool Destroyed;
    }
}
