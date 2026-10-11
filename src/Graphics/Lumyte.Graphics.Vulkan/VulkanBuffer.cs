using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanBuffer<T> : IGraphicsBuffer<T>, IShaderRawBuffer
    where T : unmanaged
{
    private readonly VulkanDevice _owner;
    private readonly VkBuffer _native;
    private readonly DeviceMemory _memory;
    private byte* _pointer;
    private bool _mapped;
    private bool _pending;
    private bool _disposed;

    internal VulkanBuffer(VulkanDevice owner, BufferDesc<T> desc, BufferLayout<T> layout, ulong size)
    {
        (_owner, Layout, Count, SizeInBytes, Usage, Memory) = (owner, layout, desc.Count, size, desc.Usage, desc.Memory);
        BufferUsageFlags usage = 0;
        if ((Usage & BufferUsage.CopySource) != 0)
        {
            usage |= BufferUsageFlags.TransferSrcBit;
        }

        if ((Usage & BufferUsage.CopyDestination) != 0)
        {
            usage |= BufferUsageFlags.TransferDstBit;
        }

        if ((Usage & (BufferUsage.ShaderRead | BufferUsage.ShaderWrite)) != 0)
        {
            usage |= BufferUsageFlags.StorageBufferBit | BufferUsageFlags.ShaderDeviceAddressBit;
        }

        if ((Usage & BufferUsage.Index) != 0)
        {
            usage |= BufferUsageFlags.IndexBufferBit;
        }

        if ((Usage & BufferUsage.Indirect) != 0)
        {
            usage |= BufferUsageFlags.IndirectBufferBit;
        }

        VkBuffer buffer = default;
        DeviceMemory memory = default;
        try
        {
            var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = usage, SharingMode = SharingMode.Exclusive };
            Check(owner.Api.CreateBuffer(owner.NativeDevice, &info, null, &buffer), "CreateBuffer");
            MemoryRequirements requirements = default;
            owner.Api.GetBufferMemoryRequirements(owner.NativeDevice, buffer, &requirements);
            PhysicalDeviceMemoryProperties properties = default;
            owner.Api.GetPhysicalDeviceMemoryProperties(owner.PhysicalDevice, &properties);
            MemoryPropertyFlags required = Memory == MemoryPreference.Automatic ? MemoryPropertyFlags.DeviceLocalBit : MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
            uint type = SelectMemoryType(properties, requirements.MemoryTypeBits, required);
            var flags = new MemoryAllocateFlagsInfo { SType = StructureType.MemoryAllocateFlagsInfo, Flags = (Usage & (BufferUsage.ShaderRead | BufferUsage.ShaderWrite)) != 0 ? MemoryAllocateFlags.DeviceAddressBit : 0 };
            var allocation = new MemoryAllocateInfo { PNext = &flags, SType = StructureType.MemoryAllocateInfo, AllocationSize = requirements.Size, MemoryTypeIndex = type };
            Check(owner.Api.AllocateMemory(owner.NativeDevice, &allocation, null, &memory), "AllocateMemory");
            Check(owner.Api.BindBufferMemory(owner.NativeDevice, buffer, memory, 0), "BindBufferMemory");
            (_native, _memory) = (buffer, memory);
        }
        catch
        {
            if (buffer.Handle != 0)
            {
                owner.Api.DestroyBuffer(owner.NativeDevice, buffer, null);
            }

            if (memory.Handle != 0)
            {
                owner.Api.FreeMemory(owner.NativeDevice, memory, null);
            }

            throw;
        }
    }

    public object ShaderHandle => Native;

    public BufferLayout<T> Layout { get; }

    public ulong Count { get; }

    public ulong SizeInBytes { get; }

    public BufferUsage Usage { get; }

    public MemoryPreference Memory { get; }

    public bool IsMapped => _mapped && !_pending && !_disposed;

    internal VkBuffer Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_mapped || _pending)
            {
                throw new InvalidOperationException("GPU commands require an unmapped buffer.");
            }

            return _native;
        }
    }

    internal VulkanDevice Owner => _owner;

    public BufferSlice<T> Slice(ulong offset, ulong count) => new(this, offset, count);

    public void CopyFrom(ReadOnlySpan<T> source) => Slice(0, Count).CopyFrom(source);

    public void CopyTo(Span<T> destination) => Slice(0, Count).CopyTo(destination);

    public void ValidateRange(ulong offset, ulong length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
    }

    public ValueTask MapAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Memory == MemoryPreference.Automatic || _mapped || _pending)
        {
            throw new InvalidOperationException("Buffer cannot begin a CPU mapping in its current state.");
        }

        _pending = true;
        try
        {
            void* pointer = null;
            Check(_owner.Api.MapMemory(_owner.NativeDevice, _memory, 0, SizeInBytes, 0, &pointer), "MapMemory");
            _pointer = (byte*)pointer;
            _mapped = true;
            if (cancellationToken.IsCancellationRequested)
            {
                UnmapCore();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return ValueTask.CompletedTask;
        }
        finally
        {
            _pending = false;
        }
    }

    public void Unmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_mapped || _pending)
        {
            throw new InvalidOperationException("No completed mapping is available.");
        }

        UnmapCore();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_pending)
        {
            throw new InvalidOperationException("Wait for the mapping request before disposal.");
        }

        if (_mapped)
        {
            UnmapCore();
        }

        _owner.Api.DestroyBuffer(_owner.NativeDevice, _native, null);
        _owner.Api.FreeMemory(_owner.NativeDevice, _memory, null);
        _disposed = true;
    }

    void IGraphicsBuffer<T>.CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
    {
        ValidateRange(offset, length);
        RequireMapping(MemoryPreference.Upload);
        if ((ulong)source.Length > length)
        {
            throw new ArgumentException("Source does not fit the buffer range.", nameof(source));
        }

        source.CopyTo(new Span<byte>(_pointer + checked((int)offset), source.Length));
    }

    void IGraphicsBuffer<T>.CopyTo(Span<byte> destination, ulong offset, ulong length)
    {
        ValidateRange(offset, length);
        RequireMapping(MemoryPreference.Readback);
        if ((ulong)destination.Length < length)
        {
            throw new ArgumentException("Destination does not fit the complete buffer range.", nameof(destination));
        }

        new ReadOnlySpan<byte>(_pointer + checked((int)offset), checked((int)length)).CopyTo(destination);
    }

    private static uint SelectMemoryType(PhysicalDeviceMemoryProperties properties, uint bits, MemoryPropertyFlags required)
    {
        for (int i = 0; i < properties.MemoryTypeCount; i++)
        {
            if ((bits & (1U << i)) != 0 && (properties.MemoryTypes[i].PropertyFlags & required) == required)
            {
                return (uint)i;
            }
        }

        throw new NotSupportedException("No Vulkan memory type satisfies the required buffer access.");
    }

    private static void Check(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Vulkan {operation} failed: {result}.");
        }
    }

    private void RequireMapping(MemoryPreference memory)
    {
        if (!_mapped || _pending || Memory != memory)
        {
            throw new InvalidOperationException("CPU copy requires an explicitly mapped buffer of the correct memory direction.");
        }
    }

    private void UnmapCore()
    {
        _owner.Api.UnmapMemory(_owner.NativeDevice, _memory);
        _pointer = null;
        _mapped = false;
    }
}
