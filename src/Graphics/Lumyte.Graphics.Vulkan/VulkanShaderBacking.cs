using Silk.NET.Vulkan;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanShaderBacking : IDisposable
{
    private readonly VulkanDevice _owner;
    private V.Buffer _buffer;
    private DeviceMemory _memory;

    internal VulkanShaderBacking(VulkanDevice owner, int size)
    {
        _owner = owner;
        Size = checked((ulong)size);
        try
        {
            var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = Size, Usage = BufferUsageFlags.UniformBufferBit | BufferUsageFlags.StorageBufferBit | BufferUsageFlags.ShaderDeviceAddressBit, SharingMode = SharingMode.Exclusive };
            Check(owner.Api.CreateBuffer(owner.NativeDevice, &info, null, out _buffer));
            owner.Api.GetBufferMemoryRequirements(owner.NativeDevice, _buffer, out MemoryRequirements requirements);
            owner.Api.GetPhysicalDeviceMemoryProperties(owner.PhysicalDevice, out PhysicalDeviceMemoryProperties properties);
            uint selected = uint.MaxValue;
            for (uint i = 0; i < properties.MemoryTypeCount; i++)
            {
                const MemoryPropertyFlags Required = MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
                if ((requirements.MemoryTypeBits & (1u << (int)i)) != 0 && (properties.MemoryTypes[(int)i].PropertyFlags & Required) == Required)
                {
                    selected = i;
                    break;
                }
            }

            if (selected == uint.MaxValue)
            {
                throw new NotSupportedException("Shader snapshots require host-visible coherent Vulkan memory.");
            }

            var flags = new MemoryAllocateFlagsInfo { SType = StructureType.MemoryAllocateFlagsInfo, Flags = MemoryAllocateFlags.DeviceAddressBit };
            var allocation = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, PNext = &flags, AllocationSize = requirements.Size, MemoryTypeIndex = selected };
            Check(owner.Api.AllocateMemory(owner.NativeDevice, &allocation, null, out _memory));
            Check(owner.Api.BindBufferMemory(owner.NativeDevice, _buffer, _memory, 0));
            var address = new BufferDeviceAddressInfo { SType = StructureType.BufferDeviceAddressInfo, Buffer = _buffer };
            Address = owner.Api.GetBufferDeviceAddress(owner.NativeDevice, &address);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal V.Buffer Native => _buffer;

    internal ulong Size { get; }

    internal ulong Address { get; }

    public void Dispose()
    {
        if (_buffer.Handle != 0)
        {
            _owner.Api.DestroyBuffer(_owner.NativeDevice, _buffer, null);
            _buffer = default;
        }

        if (_memory.Handle != 0)
        {
            _owner.Api.FreeMemory(_owner.NativeDevice, _memory, null);
            _memory = default;
        }
    }

    internal void Set(byte[] bytes)
    {
        if ((ulong)bytes.Length != Size)
        {
            throw new ArgumentException("Snapshot allocation size differs from packed values.");
        }

        void* pointer;
        Check(_owner.Api.MapMemory(_owner.NativeDevice, _memory, 0, Size, 0, &pointer));
        try
        {
            bytes.CopyTo(new Span<byte>(pointer, bytes.Length));
        }
        finally
        {
            _owner.Api.UnmapMemory(_owner.NativeDevice, _memory);
        }
    }

    private static void Check(Result result)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan shader backing operation failed: {result}.");
        }
    }
}
