namespace Lumyte.Graphics;

public readonly record struct GpuFenceValue(ulong Value) : IComparable<GpuFenceValue>
{
    public int CompareTo(GpuFenceValue other) => Value.CompareTo(other.Value);
}

/// <summary>
/// Persistent allocation owner. Allocations are released only after a reported completed fence.
/// </summary>
public sealed class GpuPersistentArena
{
    private readonly IGpuBackend backend;
    private readonly HashSet<GpuMemoryAllocation> liveAllocations = [];
    private readonly SortedDictionary<ulong, List<GpuMemoryAllocation>> retiredAllocations = [];

    public GpuPersistentArena(IGpuBackend backend)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        RequireExplicitPlacement(backend);
    }

    public int LiveAllocationCount => liveAllocations.Count;
    public int PendingRetirementCount => retiredAllocations.Values.Sum(static values => values.Count);

    public GpuMemoryAllocation Allocate(
        ulong size,
        ulong alignment = 16,
        GpuMemoryKind kind = GpuMemoryKind.HostMapped)
    {
        if (size == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if (alignment == 0 || !System.Numerics.BitOperations.IsPow2(alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }

        GpuMemoryAllocation allocation = backend.AllocateMemory(size, alignment, kind).Validate();
        if (allocation.Size < size || allocation.Alignment < alignment)
        {
            backend.FreeMemory(allocation);
            throw new InvalidOperationException("Backend allocation does not satisfy the requested size and alignment.");
        }

        if (!liveAllocations.Add(allocation))
        {
            backend.FreeMemory(allocation);
            throw new InvalidOperationException("Backend returned a duplicate live allocation.");
        }

        return allocation;
    }

    public void Retire(GpuMemoryAllocation allocation, GpuFenceValue afterFence)
    {
        if (!liveAllocations.Remove(allocation))
        {
            throw new ArgumentException("Allocation is not live in this arena.", nameof(allocation));
        }

        if (!retiredAllocations.TryGetValue(afterFence.Value, out List<GpuMemoryAllocation>? values))
        {
            values = [];
            retiredAllocations.Add(afterFence.Value, values);
        }

        values.Add(allocation);
    }

    public int Collect(GpuFenceValue completedFence)
    {
        ulong[] completedKeys = retiredAllocations.Keys
            .TakeWhile(value => value <= completedFence.Value)
            .ToArray();
        int freed = 0;

        foreach (ulong key in completedKeys)
        {
            foreach (GpuMemoryAllocation allocation in retiredAllocations[key])
            {
                backend.FreeMemory(allocation);
                freed++;
            }

            retiredAllocations.Remove(key);
        }

        return freed;
    }

    public void VerifyEmpty()
    {
        if (liveAllocations.Count != 0 || retiredAllocations.Count != 0)
        {
            throw new InvalidOperationException("Arena still owns live or fence-pending allocations.");
        }
    }

    private static void RequireExplicitPlacement(IGpuBackend backend)
    {
        if ((backend.Capabilities & GpuBackendCapabilities.ExplicitPlacement) == 0)
        {
            throw new ArgumentException("Backend does not support explicit resource placement.", nameof(backend));
        }
    }
}

/// <summary>
/// Manual placed-texture path. Texture handles and allocations remain independent values.
/// </summary>
public sealed class GpuManualTextureAllocator
{
    private readonly IGpuBackend backend;
    private readonly GpuPersistentArena arena;
    private readonly HashSet<GpuTextureHandle> liveTextures = [];
    private readonly SortedDictionary<ulong, List<GpuTextureHandle>> retiredTextures = [];

    public GpuManualTextureAllocator(IGpuBackend backend, GpuPersistentArena arena)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.arena = arena ?? throw new ArgumentNullException(nameof(arena));
    }

    public GpuTextureMemoryRequirements GetMemoryRequirements(GpuTextureDescription description)
    {
        description.Validate();
        return backend.GetTextureMemoryRequirements(description).Validate();
    }

    public GpuMemoryAllocation AllocateMemory(GpuTextureDescription description)
    {
        GpuTextureMemoryRequirements requirements = GetMemoryRequirements(description);
        return arena.Allocate(requirements.Size, requirements.Alignment, GpuMemoryKind.DeviceLocal);
    }

    public GpuTextureHandle CreatePlacedTexture(
        GpuTextureDescription description,
        GpuMemoryAllocation allocation)
    {
        description.Validate();
        allocation.Validate();
        GpuTextureMemoryRequirements requirements = GetMemoryRequirements(description);
        if (allocation.Kind != GpuMemoryKind.DeviceLocal)
        {
            throw new ArgumentException("Textures require GPU-only memory.", nameof(allocation));
        }

        if (allocation.Size < requirements.Size || allocation.Alignment < requirements.Alignment)
        {
            throw new ArgumentException("Allocation does not satisfy texture memory requirements.", nameof(allocation));
        }

        GpuTextureHandle texture = backend.CreatePlacedTexture(description, allocation);
        if (texture.IsNull || !liveTextures.Add(texture))
        {
            if (!texture.IsNull)
            {
                backend.DestroyTexture(texture);
            }

            throw new InvalidOperationException("Backend returned an invalid or duplicate texture handle.");
        }

        return texture;
    }

    public GpuTextureView CreateView(
        GpuTextureHandle texture,
        GpuTextureViewDescription description)
    {
        RequireLive(texture);
        return backend.CreateTextureView(texture, description);
    }

    public GpuColorAttachment ColorAttachment(
        GpuTextureView view,
        GpuAttachmentLoadOperation loadOperation,
        GpuAttachmentStoreOperation storeOperation,
        GpuClearColor clearColor = default)
    {
        RequireLive(view.Texture);
        if (!GpuFormatInfo.IsColor(view.Description.Format))
        {
            throw new ArgumentException("Color attachment view must use a color format.", nameof(view));
        }

        return new(view, loadOperation, storeOperation, clearColor);
    }

    public GpuDepthStencilAttachment DepthStencilAttachment(
        GpuTextureView view,
        GpuAttachmentLoadOperation loadOperation,
        GpuAttachmentStoreOperation storeOperation,
        GpuClearDepthStencil clearValue = default)
    {
        RequireLive(view.Texture);
        if (!GpuFormatInfo.IsDepthStencilAttachment(view.Description.Format))
        {
            throw new ArgumentException("Depth-stencil attachment view must use a depth-stencil format.", nameof(view));
        }

        return new(view, loadOperation, storeOperation, clearValue);
    }

    /// <summary>Retires native texture metadata before its separate memory allocation at the same fence.</summary>
    public void Retire(
        GpuTextureHandle texture,
        GpuMemoryAllocation allocation,
        GpuFenceValue afterFence)
    {
        RequireLive(texture);
        arena.Retire(allocation, afterFence);
        liveTextures.Remove(texture);
        if (!retiredTextures.TryGetValue(afterFence.Value, out List<GpuTextureHandle>? values))
        {
            values = [];
            retiredTextures.Add(afterFence.Value, values);
        }

        values.Add(texture);
    }

    public int Collect(GpuFenceValue completedFence)
    {
        ulong[] completedKeys = retiredTextures.Keys
            .TakeWhile(value => value <= completedFence.Value)
            .ToArray();
        int destroyed = 0;

        foreach (ulong key in completedKeys)
        {
            foreach (GpuTextureHandle texture in retiredTextures[key])
            {
                backend.DestroyTexture(texture);
                destroyed++;
            }

            retiredTextures.Remove(key);
        }

        arena.Collect(completedFence);
        return destroyed;
    }

    public void VerifyEmpty()
    {
        if (liveTextures.Count != 0 || retiredTextures.Count != 0)
        {
            throw new InvalidOperationException("Texture allocator still owns live or fence-pending textures.");
        }

        arena.VerifyEmpty();
    }

    private void RequireLive(GpuTextureHandle texture)
    {
        if (texture.IsNull || !liveTextures.Contains(texture))
        {
            throw new ArgumentException("Texture is not live in this allocator.", nameof(texture));
        }
    }
}

/// <summary>Manual placed-buffer path sharing the persistent memory arena.</summary>
public sealed class GpuManualBufferAllocator
{
    private readonly IGpuBackend backend;
    private readonly GpuPersistentArena arena;
    private readonly HashSet<GpuBufferHandle> liveBuffers = [];
    private readonly SortedDictionary<ulong, List<GpuBufferHandle>> retiredBuffers = [];

    public GpuManualBufferAllocator(IGpuBackend backend, GpuPersistentArena arena)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.arena = arena ?? throw new ArgumentNullException(nameof(arena));
    }

    public GpuBufferMemoryRequirements GetMemoryRequirements(GpuBufferDescription description)
    {
        description.Validate();
        return backend.GetBufferMemoryRequirements(description).Validate();
    }

    public GpuMemoryAllocation AllocateMemory(
        GpuBufferDescription description,
        GpuMemoryKind kind)
    {
        GpuBufferMemoryRequirements requirements = GetMemoryRequirements(description);
        return arena.Allocate(requirements.Size, requirements.Alignment, kind);
    }

    public GpuBufferHandle CreatePlacedBuffer(
        GpuBufferDescription description,
        GpuMemoryAllocation allocation)
    {
        description.Validate();
        allocation.Validate();
        GpuBufferMemoryRequirements requirements = GetMemoryRequirements(description);
        if (allocation.Size < requirements.Size || allocation.Alignment < requirements.Alignment)
        {
            throw new ArgumentException("Allocation does not satisfy buffer memory requirements.", nameof(allocation));
        }

        GpuBufferHandle buffer = backend.CreatePlacedBuffer(description, allocation);
        if (buffer.IsNull || !liveBuffers.Add(buffer))
        {
            if (!buffer.IsNull) { backend.DestroyBuffer(buffer); }
            throw new InvalidOperationException("Backend returned an invalid or duplicate buffer handle.");
        }

        return buffer;
    }

    public GpuMemoryAddress AddressOf(GpuBufferHandle buffer, ulong offset = 0, ulong? length = null)
    {
        if (!liveBuffers.Contains(buffer))
        {
            throw new ArgumentException("Buffer is not live in this allocator.", nameof(buffer));
        }
        if (offset > buffer.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        ulong requestedLength = length ?? buffer.Size - offset;
        if (requestedLength > buffer.Size - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
        return backend.GetBufferMemoryAddress(buffer, offset, requestedLength);
    }

    public void Retire(GpuBufferHandle buffer, GpuMemoryAllocation allocation, GpuFenceValue afterFence)
    {
        if (!liveBuffers.Contains(buffer))
        {
            throw new ArgumentException("Buffer is not live in this allocator.", nameof(buffer));
        }

        arena.Retire(allocation, afterFence);
        liveBuffers.Remove(buffer);
        if (!retiredBuffers.TryGetValue(afterFence.Value, out List<GpuBufferHandle>? values))
        {
            values = [];
            retiredBuffers.Add(afterFence.Value, values);
        }

        values.Add(buffer);
    }

    public int Collect(GpuFenceValue completedFence)
    {
        ulong[] completedKeys = retiredBuffers.Keys
            .TakeWhile(value => value <= completedFence.Value)
            .ToArray();
        int destroyed = 0;
        foreach (ulong key in completedKeys)
        {
            foreach (GpuBufferHandle buffer in retiredBuffers[key])
            {
                backend.DestroyBuffer(buffer);
                destroyed++;
            }

            retiredBuffers.Remove(key);
        }

        arena.Collect(completedFence);
        return destroyed;
    }
}
