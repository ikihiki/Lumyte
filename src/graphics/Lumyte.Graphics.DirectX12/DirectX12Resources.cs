using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Device
{
    private const ulong HeapAlignment = D3D12.DefaultResourcePlacementAlignment;
    private const ulong BufferHeapCompatibility = 1;
    private const ulong TextureHeapCompatibility = 2;
    private const ulong RenderTargetHeapCompatibility = 3;

    public GpuTextureMemoryRequirements GetTextureMemoryRequirements(GpuTextureDescription description)
    {
        VerifyNotDisposed();
        description.Validate();
        ResourceDesc native = TextureDescription(description);
        ResourceAllocationInfo info = device.GetResourceAllocationInfo(0, 1, in native);
        if (info.SizeInBytes == ulong.MaxValue)
        {
            throw new NotSupportedException("Direct3D 12 rejected the texture description.");
        }

        bool renderTarget = (description.Usage
            & (GpuTextureUsage.ColorAttachment | GpuTextureUsage.DepthStencilAttachment)) != 0;
        return new(
            info.SizeInBytes,
            info.Alignment,
            renderTarget ? RenderTargetHeapCompatibility : TextureHeapCompatibility);
    }

    public GpuBufferMemoryRequirements GetBufferMemoryRequirements(GpuBufferDescription description)
    {
        VerifyNotDisposed();
        description.Validate();
        ResourceDesc native = BufferDescription(description.Size);
        ResourceAllocationInfo info = device.GetResourceAllocationInfo(0, 1, in native);
        return new(info.SizeInBytes, info.Alignment, BufferHeapCompatibility);
    }

    public GpuMemoryAllocation AllocateMemory(ulong size, ulong alignment, GpuMemoryKind kind)
        => AllocateMemory(size, alignment, kind, 0);

    public GpuMemoryAllocation AllocateMemory(
        ulong size,
        ulong alignment,
        GpuMemoryKind kind,
        ulong compatibility)
    {
        VerifyNotDisposed();
        if (size == 0) { throw new ArgumentOutOfRangeException(nameof(size)); }
        if (alignment == 0 || !System.Numerics.BitOperations.IsPow2(alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }
        if (!Enum.IsDefined(kind)) { throw new ArgumentOutOfRangeException(nameof(kind)); }

        ulong actualAlignment = alignment <= HeapAlignment
            ? HeapAlignment
            : alignment <= D3D12.DefaultMsaaResourcePlacementAlignment
                ? (ulong)D3D12.DefaultMsaaResourcePlacementAlignment
                : throw new NotSupportedException("Direct3D 12 heap alignment cannot exceed 4 MiB.");
        ulong actualSize = Align(size, actualAlignment);
        HeapType heapType = kind switch
        {
            GpuMemoryKind.DeviceLocal => HeapType.Default,
            GpuMemoryKind.HostMapped => HeapType.Upload,
            GpuMemoryKind.HostCached => HeapType.Readback,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        HeapFlags flags = kind == GpuMemoryKind.DeviceLocal
            ? compatibility switch
            {
                0 => HeapFlags.None,
                BufferHeapCompatibility => HeapFlags.AllowOnlyBuffers,
                TextureHeapCompatibility => HeapFlags.AllowOnlyNonRTDSTextures,
                RenderTargetHeapCompatibility => HeapFlags.AllowOnlyRTDSTextures,
                _ => throw new ArgumentOutOfRangeException(nameof(compatibility)),
            }
            : compatibility is 0 or BufferHeapCompatibility
                ? HeapFlags.AllowOnlyBuffers
                : throw new ArgumentOutOfRangeException(nameof(compatibility));
        ComPtr<ID3D12Heap> heap = CreateHeap(heapType, actualSize, flags, actualAlignment);
        ComPtr<ID3D12Resource> mappedResource = default;
        nint cpuAddress = 0;
        try
        {
            if (kind != GpuMemoryKind.DeviceLocal)
            {
                ResourceDesc buffer = BufferDescription(actualSize);
                ResourceStates state = kind == GpuMemoryKind.HostMapped
                    ? ResourceStates.GenericRead
                    : ResourceStates.CopyDest;
                SilkMarshal.ThrowHResult(device.CreatePlacedResource<ID3D12Heap, ID3D12Resource>(
                    heap, 0, in buffer, state, null, out mappedResource));
                void* mapped = null;
                if (kind == GpuMemoryKind.HostCached)
                {
                    Silk.NET.Direct3D12.Range readRange = new(0, checked((nuint)actualSize));
                    SilkMarshal.ThrowHResult(mappedResource.Map(0, &readRange, &mapped));
                }
                else
                {
                    SilkMarshal.ThrowHResult(mappedResource.Map(0, (Silk.NET.Direct3D12.Range*)null, &mapped));
                }
                cpuAddress = (nint)mapped;
            }

            ulong id = NextHandle();
            var record = new MemoryRecord(
                heap,
                mappedResource,
                kind,
                actualSize,
                cpuAddress,
                compatibility);
            memories.Add(id, record);
            return new(actualSize, actualAlignment, kind, cpuAddress, new(id, 0, actualSize));
        }
        catch
        {
            if (cpuAddress != 0) { mappedResource.Unmap(0, (Silk.NET.Direct3D12.Range*)null); }
            mappedResource.Dispose();
            heap.Dispose();
            throw;
        }
    }

    public void FreeMemory(GpuMemoryAllocation allocation)
    {
        VerifyNotDisposed();
        allocation.Validate();
        ulong id = allocation.MemoryAddress.AllocationId;
        if (!memories.TryGetValue(id, out MemoryRecord? record) || !record.MatchesRoot(allocation))
        {
            throw new ArgumentException("Allocation does not belong to this Direct3D 12 device.", nameof(allocation));
        }
        if (record.BoundResourceCount != 0)
        {
            throw new InvalidOperationException("Allocation still has a live placed resource.");
        }

        memories.Remove(id);
        record.Dispose();
    }

    public GpuTextureHandle CreatePlacedTexture(GpuTextureDescription description, GpuMemoryAllocation allocation)
    {
        VerifyNotDisposed();
        description.Validate();
        allocation.Validate();
        if (allocation.Kind != GpuMemoryKind.DeviceLocal)
        {
            throw new ArgumentException("Textures require device-local memory.", nameof(allocation));
        }
        MemoryRecord memory = RequireMemory(allocation);
        GpuTextureMemoryRequirements requirements = GetTextureMemoryRequirements(description);
        if (!memory.IsCompatible(requirements.Compatibility))
        {
            throw new ArgumentException("Allocation uses an incompatible Direct3D 12 heap class.", nameof(allocation));
        }
        ValidatePlacement(allocation, requirements.Size, requirements.Alignment);

        ResourceDesc native = TextureDescription(description);
        ComPtr<ID3D12Resource> resource = default;
        try
        {
            SilkMarshal.ThrowHResult(device.CreatePlacedResource<ID3D12Heap, ID3D12Resource>(
                memory.Heap,
                allocation.MemoryAddress.Offset,
                in native,
                ResourceStates.Common,
                null,
                out resource));
            ulong id = NextHandle();
            textures.Add(id, new(
                resource,
                description,
                allocation.MemoryAddress.AllocationId,
                allocation.MemoryAddress.Offset));
            memory.BoundResourceCount++;
            return new(id);
        }
        catch
        {
            resource.Dispose();
            throw;
        }
    }

    public void DestroyTexture(GpuTextureHandle texture)
    {
        VerifyNotDisposed();
        if (!textures.Remove(texture.Value, out TextureRecord? record))
        {
            throw new ArgumentException("Texture does not belong to this Direct3D 12 device.", nameof(texture));
        }
        if (textureViews.Values.Any(view => view.Texture.Value == texture.Value))
        {
            textures.Add(texture.Value, record);
            throw new InvalidOperationException("Texture still has a live view.");
        }
        record.Dispose();
        memories[record.AllocationId].BoundResourceCount--;
    }

    public GpuTextureView CreateTextureView(GpuTextureHandle texture, GpuTextureViewDescription description)
    {
        VerifyNotDisposed();
        TextureRecord record = RequireTexture(texture);
        uint mipCount = description.MipCount == uint.MaxValue
            ? record.Description.MipCount - description.BaseMip
            : description.MipCount;
        uint layerCount = description.LayerCount == uint.MaxValue
            ? record.Description.LayerCount - description.BaseLayer
            : description.LayerCount;
        if (description.BaseMip >= record.Description.MipCount || mipCount == 0
            || mipCount > record.Description.MipCount - description.BaseMip
            || description.BaseLayer >= record.Description.LayerCount || layerCount == 0
            || layerCount > record.Description.LayerCount - description.BaseLayer)
        {
            throw new ArgumentOutOfRangeException(nameof(description));
        }

        var normalized = description with { MipCount = mipCount, LayerCount = layerCount };
        ComPtr<ID3D12DescriptorHeap> attachmentHeap = default;
        DescriptorHeapType? attachmentType = null;
        if ((record.Description.Usage & GpuTextureUsage.ColorAttachment) != 0)
        {
            attachmentType = DescriptorHeapType.Rtv;
        }
        else if ((record.Description.Usage & GpuTextureUsage.DepthStencilAttachment) != 0)
        {
            attachmentType = DescriptorHeapType.Dsv;
        }

        try
        {
            if (attachmentType is { } type)
            {
                attachmentHeap = CreateDescriptorHeap(type, 1, false);
                CpuDescriptorHandle handle = attachmentHeap.GetCPUDescriptorHandleForHeapStart();
                if (type == DescriptorHeapType.Rtv)
                {
                    device.CreateRenderTargetView(record.Resource, null, handle);
                }
                else
                {
                    device.CreateDepthStencilView(record.Resource, null, handle);
                }
            }

            ulong id = NextHandle();
            var view = new GpuTextureView(new(id), texture, normalized);
            textureViews.Add(id, new(view, attachmentHeap, attachmentType));
            return view;
        }
        catch
        {
            attachmentHeap.Dispose();
            throw;
        }
    }

    public void DestroyTextureView(GpuTextureView view)
    {
        VerifyNotDisposed();
        if (!textureViews.TryGetValue(view.Id.Value, out TextureViewRecord? record) || record.View != view)
        {
            throw new ArgumentException("Texture view does not belong to this Direct3D 12 device.", nameof(view));
        }
        textureViews.Remove(view.Id.Value);
        record.Dispose();
    }

    public SamplerId CreateSampler(GpuSamplerDescription description)
    {
        VerifyNotDisposed();
        description.Validate();
        ulong id = NextHandle();
        samplers.Add(id, description);
        return new(id);
    }

    public void DestroySampler(SamplerId sampler)
    {
        VerifyNotDisposed();
        if (!samplers.Remove(sampler.Value))
        {
            throw new ArgumentException("Sampler does not belong to this Direct3D 12 device.", nameof(sampler));
        }
    }

    public GpuBufferHandle CreatePlacedBuffer(GpuBufferDescription description, GpuMemoryAllocation allocation)
    {
        VerifyNotDisposed();
        description.Validate();
        allocation.Validate();
        MemoryRecord memory = RequireMemory(allocation);
        GpuBufferMemoryRequirements requirements = GetBufferMemoryRequirements(description);
        if (!memory.IsCompatible(requirements.Compatibility))
        {
            throw new ArgumentException("Allocation uses an incompatible Direct3D 12 heap class.", nameof(allocation));
        }
        ValidatePlacement(allocation, requirements.Size, requirements.Alignment);
        if (allocation.Kind == GpuMemoryKind.HostMapped && (description.Usage & GpuBufferUsage.CopySource) == 0)
        {
            throw new ArgumentException("Upload memory buffers must support copy source usage.", nameof(description));
        }
        if (allocation.Kind == GpuMemoryKind.HostCached && (description.Usage & GpuBufferUsage.CopyDestination) == 0)
        {
            throw new ArgumentException("Readback memory buffers must support copy destination usage.", nameof(description));
        }

        ComPtr<ID3D12Resource> resource = memory.MappedResource;
        bool ownsResource = allocation.Kind == GpuMemoryKind.DeviceLocal;
        try
        {
            if (ownsResource)
            {
                ResourceDesc native = BufferDescription(description.Size);
                ResourceStates initial = (description.Usage & GpuBufferUsage.CopyDestination) != 0
                    ? ResourceStates.CopyDest
                    : ResourceStates.Common;
                SilkMarshal.ThrowHResult(device.CreatePlacedResource<ID3D12Heap, ID3D12Resource>(
                    memory.Heap,
                    allocation.MemoryAddress.Offset,
                    in native,
                    initial,
                    null,
                    out resource));
            }
            ulong id = NextHandle();
            buffers.Add(id, new(
                resource,
                description,
                allocation.MemoryAddress.AllocationId,
                allocation.MemoryAddress.Offset,
                ownsResource));
            memory.BoundResourceCount++;
            return new(id, description.Size);
        }
        catch
        {
            if (ownsResource) { resource.Dispose(); }
            throw;
        }
    }

    public GpuMemoryAddress GetBufferMemoryAddress(GpuBufferHandle buffer, ulong offset, ulong length)
    {
        VerifyNotDisposed();
        BufferRecord record = RequireBuffer(buffer);
        if (offset > record.Description.Size || length > record.Description.Size - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        return new(record.AllocationId, checked(record.AllocationOffset + offset), length);
    }

    public void WriteBuffer(GpuBufferHandle buffer, ReadOnlySpan<byte> source)
        => WriteBuffer(buffer, 0, source);

    public void WriteBuffer(
        GpuBufferHandle buffer,
        ulong destinationOffset,
        ReadOnlySpan<byte> source)
    {
        VerifyNotDisposed();
        BufferRecord record = RequireBuffer(buffer);
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

    public void DestroyBuffer(GpuBufferHandle buffer)
    {
        VerifyNotDisposed();
        if (!buffers.TryGetValue(buffer.Value, out BufferRecord? record) || record.Description.Size != buffer.Size)
        {
            throw new ArgumentException("Buffer does not belong to this Direct3D 12 device.", nameof(buffer));
        }
        if (bufferViews.Values.Any(view => view.View.Buffer == buffer))
        {
            throw new InvalidOperationException("Buffer still has a live view.");
        }
        buffers.Remove(buffer.Value);
        record.Dispose();
        memories[record.AllocationId].BoundResourceCount--;
    }

    public GpuBufferView CreateBufferView(
        GpuBufferHandle buffer,
        GpuBufferViewDescription description)
    {
        VerifyNotDisposed();
        BufferRecord record = RequireBuffer(buffer);
        if ((record.Description.Usage & GpuBufferUsage.ShaderData) == 0)
        {
            throw new ArgumentException("Buffer views require ShaderData usage.", nameof(buffer));
        }
        GpuBufferViewDescription normalized = description.Normalize(buffer);
        ulong id = NextHandle();
        var view = new GpuBufferView(new(id), buffer, normalized);
        bufferViews.Add(id, new(view));
        return view;
    }

    public void DestroyBufferView(GpuBufferView view)
    {
        VerifyNotDisposed();
        if (!bufferViews.Remove(view.Id.Value, out BufferViewRecord? record) || record.View != view)
        {
            if (record is not null) { bufferViews.Add(view.Id.Value, record); }
            throw new ArgumentException("Buffer view does not belong to this Direct3D 12 device.", nameof(view));
        }
    }

    private ComPtr<ID3D12DescriptorHeap> CreateDescriptorHeap(DescriptorHeapType type, uint capacity, bool shaderVisible)
    {
        var description = new DescriptorHeapDesc(
            type, capacity, shaderVisible ? DescriptorHeapFlags.ShaderVisible : DescriptorHeapFlags.None, 0);
        SilkMarshal.ThrowHResult(device.CreateDescriptorHeap<ID3D12DescriptorHeap>(in description, out ComPtr<ID3D12DescriptorHeap> heap));
        return heap;
    }

    private MemoryRecord RequireMemory(GpuMemoryAllocation allocation)
    {
        if (!memories.TryGetValue(allocation.MemoryAddress.AllocationId, out MemoryRecord? record)
            || !record.MatchesRegion(allocation))
        {
            throw new ArgumentException("Allocation does not belong to this Direct3D 12 device.", nameof(allocation));
        }
        return record;
    }

    private TextureRecord RequireTexture(GpuTextureHandle texture)
    {
        if (!textures.TryGetValue(texture.Value, out TextureRecord? record))
        {
            throw new ArgumentException("Texture does not belong to this Direct3D 12 device.", nameof(texture));
        }
        return record;
    }

    private BufferRecord RequireBuffer(GpuBufferHandle buffer)
    {
        if (!buffers.TryGetValue(buffer.Value, out BufferRecord? record) || record.Description.Size != buffer.Size)
        {
            throw new ArgumentException("Buffer does not belong to this Direct3D 12 device.", nameof(buffer));
        }
        return record;
    }

    private static void ValidatePlacement(GpuMemoryAllocation allocation, ulong size, ulong alignment)
    {
        if (allocation.Size < size
            || allocation.Alignment < alignment
            || allocation.MemoryAddress.Offset % alignment != 0)
        {
            throw new ArgumentException("Allocation does not satisfy resource memory requirements.", nameof(allocation));
        }
    }

    private static ResourceDesc BufferDescription(ulong size) => new(
        ResourceDimension.Buffer, 0, size, 1, 1, 1, Format.FormatUnknown,
        new SampleDesc(1, 0), TextureLayout.LayoutRowMajor, ResourceFlags.None);

    private static ResourceDesc TextureDescription(GpuTextureDescription description) => new(
        ResourceDimension.Texture2D,
        0,
        description.Width,
        description.Height,
        checked((ushort)description.LayerCount),
        checked((ushort)description.MipCount),
        ToDxgiFormat(description.Format),
        new SampleDesc(description.SampleCount, 0),
        TextureLayout.LayoutUnknown,
        ToResourceFlags(description.Usage));

    private static ResourceFlags ToResourceFlags(GpuTextureUsage usage)
    {
        ResourceFlags result = ResourceFlags.None;
        if ((usage & GpuTextureUsage.ColorAttachment) != 0) { result |= ResourceFlags.AllowRenderTarget; }
        if ((usage & GpuTextureUsage.DepthStencilAttachment) != 0) { result |= ResourceFlags.AllowDepthStencil; }
        if ((usage & GpuTextureUsage.Storage) != 0) { result |= ResourceFlags.AllowUnorderedAccess; }
        return result;
    }

    private static Format ToDxgiFormat(GpuFormat format) => format switch
    {
        GpuFormat.Rgba8Unorm => Format.FormatR8G8B8A8Unorm,
        GpuFormat.Bgra8Unorm => Format.FormatB8G8R8A8Unorm,
        GpuFormat.R32Float => Format.FormatR32Float,
        GpuFormat.D32Float => Format.FormatD32Float,
        GpuFormat.Rgba8UnormSrgb => Format.FormatR8G8B8A8UnormSrgb,
        GpuFormat.Bgra8UnormSrgb => Format.FormatB8G8R8A8UnormSrgb,
        GpuFormat.R8Unorm => Format.FormatR8Unorm,
        GpuFormat.Rg8Unorm => Format.FormatR8G8Unorm,
        GpuFormat.Depth24PlusStencil8 => Format.FormatD24UnormS8Uint,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private const uint DefaultShaderComponentMapping = 5768;

    private static Filter ToFilter(GpuSamplerDescription description) =>
        (description.MinFilter, description.MagFilter) switch
        {
            (GpuSamplerFilter.Nearest, GpuSamplerFilter.Nearest) => Filter.MinMagMipPoint,
            (GpuSamplerFilter.Nearest, GpuSamplerFilter.Linear) => Filter.MinPointMagLinearMipPoint,
            (GpuSamplerFilter.Linear, GpuSamplerFilter.Nearest) => Filter.MinLinearMagMipPoint,
            (GpuSamplerFilter.Linear, GpuSamplerFilter.Linear) => Filter.MinMagLinearMipPoint,
            _ => throw new ArgumentOutOfRangeException(nameof(description)),
        };

    private static TextureAddressMode ToAddressMode(GpuSamplerAddressMode mode) => mode switch
    {
        GpuSamplerAddressMode.ClampToEdge => TextureAddressMode.Clamp,
        GpuSamplerAddressMode.Repeat => TextureAddressMode.Wrap,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private CpuDescriptorHandle Offset(CpuDescriptorHandle start, uint index, DescriptorHeapType type)
        => new(start.Ptr + checked((nuint)(index * device.GetDescriptorHandleIncrementSize(type))));

    private GpuDescriptorHandle Offset(GpuDescriptorHandle start, uint index, DescriptorHeapType type)
        => new(start.Ptr + checked((ulong)index * device.GetDescriptorHandleIncrementSize(type)));

    private ulong NextHandle() => nextHandle++;
    private static ulong Align(ulong value, ulong alignment) => checked((value + alignment - 1) & ~(alignment - 1));
    private void VerifyNotDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private sealed class MemoryRecord(
        ComPtr<ID3D12Heap> heap,
        ComPtr<ID3D12Resource> mappedResource,
        GpuMemoryKind kind,
        ulong size,
        nint cpuAddress,
        ulong compatibility) : IDisposable
    {
        private readonly GpuMemoryKind memoryKind = kind;
        private readonly ulong allocationSize = size;
        private readonly nint mappedAddress = cpuAddress;
        private readonly ulong heapCompatibility = compatibility;
        public ComPtr<ID3D12Heap> Heap = heap;
        public ComPtr<ID3D12Resource> MappedResource = mappedResource;
        public GpuMemoryKind Kind => memoryKind;
        public ulong Size => allocationSize;
        public nint CpuAddress => mappedAddress;
        public int BoundResourceCount;

        public bool IsCompatible(ulong required) => heapCompatibility == 0 || heapCompatibility == required;

        public bool MatchesRoot(GpuMemoryAllocation allocation) =>
            allocation.Size == allocationSize && allocation.Kind == memoryKind && allocation.CpuAddress == mappedAddress
            && allocation.MemoryAddress.Offset == 0 && allocation.MemoryAddress.Length == allocationSize;

        public bool MatchesRegion(GpuMemoryAllocation allocation) =>
            allocation.Kind == memoryKind
            && allocation.MemoryAddress.Offset <= allocationSize
            && allocation.Size <= allocationSize - allocation.MemoryAddress.Offset
            && allocation.MemoryAddress.Length >= allocation.Size
            && allocation.CpuAddress == (mappedAddress == 0
                ? 0
                : checked(mappedAddress + (nint)allocation.MemoryAddress.Offset));

        public void Dispose()
        {
            if (mappedAddress != 0)
            {
                Silk.NET.Direct3D12.Range written = new(
                    0,
                    memoryKind == GpuMemoryKind.HostMapped ? checked((nuint)allocationSize) : 0);
                MappedResource.Unmap(0, &written);
            }
            MappedResource.Dispose();
            Heap.Dispose();
        }
    }

    private sealed class TextureRecord(
        ComPtr<ID3D12Resource> resource,
        GpuTextureDescription description,
        ulong allocationId,
        ulong allocationOffset) : IDisposable
    {
        public ComPtr<ID3D12Resource> Resource = resource;
        public GpuTextureDescription Description { get; } = description;
        public ulong AllocationId { get; } = allocationId;
        public ulong AllocationOffset { get; } = allocationOffset;
        public ResourceStates State { get; set; } = ResourceStates.Common;
        public void Dispose() => Resource.Dispose();
    }

    private sealed class TextureViewRecord(
        GpuTextureView view,
        ComPtr<ID3D12DescriptorHeap> attachmentHeap,
        DescriptorHeapType? attachmentType) : IDisposable
    {
        public GpuTextureView View { get; } = view;
        public GpuTextureHandle Texture => View.Texture;
        public ComPtr<ID3D12DescriptorHeap> AttachmentHeap = attachmentHeap;
        public DescriptorHeapType? AttachmentType { get; } = attachmentType;
        public CpuDescriptorHandle AttachmentHandle => AttachmentHeap.GetCPUDescriptorHandleForHeapStart();
        public void Dispose() => AttachmentHeap.Dispose();
    }

    private sealed class BufferRecord(
        ComPtr<ID3D12Resource> resource,
        GpuBufferDescription description,
        ulong allocationId,
        ulong allocationOffset,
        bool ownsResource) : IDisposable
    {
        public ComPtr<ID3D12Resource> Resource = resource;
        public GpuBufferDescription Description { get; } = description;
        public ulong AllocationId { get; } = allocationId;
        public ulong AllocationOffset { get; } = allocationOffset;
        public ResourceStates State { get; set; } = description.Usage.HasFlag(GpuBufferUsage.CopyDestination)
            ? ResourceStates.CopyDest
            : description.Usage.HasFlag(GpuBufferUsage.CopySource) && !ownsResource
                ? ResourceStates.GenericRead
                : ResourceStates.Common;
        public void Dispose() { if (ownsResource) { Resource.Dispose(); } }
    }

    private sealed record BufferViewRecord(GpuBufferView View);

}
