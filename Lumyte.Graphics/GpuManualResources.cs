namespace Lumyte.Graphics;

public readonly record struct GpuFenceValue(ulong Value) : IComparable<GpuFenceValue>
{
    public int CompareTo(GpuFenceValue other) => Value.CompareTo(other.Value);
}

/// <summary>
/// Persistent GPU heap owner and free-region manager. Fence collection returns regions to
/// their arena blocks; <see cref="Trim"/> releases completely unused native blocks.
/// </summary>
public sealed class GpuPersistentArena : IDisposable
{
    private const ulong DefaultBlockSize = 4 * 1024 * 1024;
    private readonly IGpuBackend backend;
    private readonly ulong blockSize;
    private readonly List<Block> blocks = [];
    private readonly HashSet<GpuMemoryAllocation> liveAllocations = [];
    private readonly SortedDictionary<ulong, List<GpuMemoryAllocation>> retiredAllocations = [];
    private bool disposed;

    public GpuPersistentArena(IGpuBackend backend, ulong blockSize = DefaultBlockSize)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        RequireExplicitPlacement(backend);
        if (blockSize == 0) { throw new ArgumentOutOfRangeException(nameof(blockSize)); }
        this.blockSize = blockSize;
    }

    public int LiveAllocationCount => liveAllocations.Count;
    public int PendingRetirementCount => retiredAllocations.Values.Sum(static values => values.Count);

    public GpuMemoryAllocation Allocate(
        ulong size,
        ulong alignment = 16,
        GpuMemoryKind kind = GpuMemoryKind.HostMapped,
        ulong compatibility = 0)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (size == 0) { throw new ArgumentOutOfRangeException(nameof(size)); }
        if (alignment == 0 || !System.Numerics.BitOperations.IsPow2(alignment))
        {
            throw new ArgumentOutOfRangeException(nameof(alignment));
        }

        ulong compatibilityKey = backend.GetMemoryCompatibilityKey(kind, compatibility);

        foreach (Block block in blocks)
        {
            if (block.Kind == kind
                && backend.IsMemoryCompatibilityKeyCompatible(kind, block.Compatibility, compatibility)
                && block.TryAllocate(size, alignment, out GpuMemoryAllocation allocation))
            {
                liveAllocations.Add(allocation);
                return allocation;
            }
        }

        ulong requestedBlockSize = Math.Max(blockSize, Align(size, alignment));
        GpuMemoryAllocation backing = backend.AllocateMemory(
            requestedBlockSize,
            alignment,
            kind,
            compatibility).Validate();
        if (backing.Size < size || backing.Alignment < alignment
            || backing.MemoryAddress.Offset != 0 || backing.MemoryAddress.Length < backing.Size)
        {
            backend.FreeMemory(backing);
            throw new InvalidOperationException("Backend allocation does not satisfy the requested arena block.");
        }

        var created = new Block(backing, compatibilityKey);
        blocks.Add(created);
        if (!created.TryAllocate(size, alignment, out GpuMemoryAllocation result)
            || !liveAllocations.Add(result))
        {
            blocks.Remove(created);
            backend.FreeMemory(backing);
            throw new InvalidOperationException("The arena could not suballocate a newly created block.");
        }
        return result;
    }

    /// <summary>Immediately returns a region that is known to be idle.</summary>
    public void Release(GpuMemoryAllocation allocation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!liveAllocations.Remove(allocation))
        {
            throw new ArgumentException("Allocation is not live in this arena.", nameof(allocation));
        }
        ReleaseRegion(allocation);
    }

    public void Retire(GpuMemoryAllocation allocation, GpuFenceValue afterFence)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
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
        ObjectDisposedException.ThrowIf(disposed, this);
        ulong[] completedKeys = retiredAllocations.Keys
            .TakeWhile(value => value <= completedFence.Value)
            .ToArray();
        int freed = 0;
        foreach (ulong key in completedKeys)
        {
            foreach (GpuMemoryAllocation allocation in retiredAllocations[key])
            {
                ReleaseRegion(allocation);
                freed++;
            }
            retiredAllocations.Remove(key);
        }
        return freed;
    }

    public int Trim()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Block[] unused = blocks.Where(block => block.IsEmpty).ToArray();
        foreach (Block block in unused)
        {
            backend.FreeMemory(block.Backing);
            blocks.Remove(block);
        }
        return unused.Length;
    }

    public void VerifyEmpty()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (liveAllocations.Count != 0 || retiredAllocations.Count != 0)
        {
            throw new InvalidOperationException("Arena still owns live or fence-pending allocations.");
        }
        Trim();
    }

    public void Dispose()
    {
        if (disposed) { return; }
        VerifyEmpty();
        disposed = true;
    }

    internal void RequireBackend(IGpuBackend candidate)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(backend, candidate))
        {
            throw new ArgumentException("Arena belongs to another GPU backend.", nameof(candidate));
        }
    }

    private void ReleaseRegion(GpuMemoryAllocation allocation)
    {
        Block? owner = blocks.SingleOrDefault(
            block => block.Backing.MemoryAddress.AllocationId == allocation.MemoryAddress.AllocationId);
        if (owner is null || !owner.Release(allocation))
        {
            throw new InvalidOperationException("Allocation does not identify a live arena region.");
        }
    }

    private static ulong Align(ulong value, ulong alignment)
        => checked((value + alignment - 1) & ~(alignment - 1));

    private static void RequireExplicitPlacement(IGpuBackend backend)
    {
        if ((backend.Capabilities & GpuBackendCapabilities.ExplicitPlacement) == 0)
        {
            throw new ArgumentException("Backend does not support explicit resource placement.", nameof(backend));
        }
    }

    private sealed class Block
    {
        private readonly List<Region> free = [];

        public Block(GpuMemoryAllocation backing, ulong compatibility)
        {
            Backing = backing;
            Compatibility = compatibility;
            free.Add(new(0, backing.Size));
        }

        public GpuMemoryAllocation Backing { get; }
        public ulong Compatibility { get; }
        public GpuMemoryKind Kind => Backing.Kind;
        public bool IsEmpty => free.Count == 1 && free[0] == new Region(0, Backing.Size);

        public bool TryAllocate(ulong size, ulong alignment, out GpuMemoryAllocation allocation)
        {
            if (Backing.Alignment < alignment)
            {
                allocation = default;
                return false;
            }

            for (int index = 0; index < free.Count; index++)
            {
                Region region = free[index];
                ulong offset = Align(region.Offset, alignment);
                if (offset < region.Offset || offset - region.Offset > region.Size
                    || size > region.Size - (offset - region.Offset))
                {
                    continue;
                }

                free.RemoveAt(index);
                ulong prefix = offset - region.Offset;
                ulong suffixOffset = checked(offset + size);
                ulong suffix = region.Size - prefix - size;
                if (suffix != 0) { free.Insert(index, new(suffixOffset, suffix)); }
                if (prefix != 0) { free.Insert(index, new(region.Offset, prefix)); }
                nint cpuAddress = Backing.CpuAddress == 0
                    ? 0
                    : checked(Backing.CpuAddress + (nint)offset);
                allocation = new(
                    size,
                    alignment,
                    Backing.Kind,
                    cpuAddress,
                    new(Backing.MemoryAddress.AllocationId, offset, size));
                return true;
            }

            allocation = default;
            return false;
        }

        public bool Release(GpuMemoryAllocation allocation)
        {
            if (allocation.Kind != Backing.Kind
                || allocation.MemoryAddress.AllocationId != Backing.MemoryAddress.AllocationId
                || allocation.MemoryAddress.Offset > Backing.Size
                || allocation.Size > Backing.Size - allocation.MemoryAddress.Offset)
            {
                return false;
            }

            free.Add(new(allocation.MemoryAddress.Offset, allocation.Size));
            free.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
            for (int index = free.Count - 1; index > 0; index--)
            {
                Region previous = free[index - 1];
                Region current = free[index];
                if (previous.Offset + previous.Size != current.Offset) { continue; }
                free[index - 1] = new(previous.Offset, checked(previous.Size + current.Size));
                free.RemoveAt(index);
            }
            return true;
        }

        private readonly record struct Region(ulong Offset, ulong Size);
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
        return arena.Allocate(
            requirements.Size,
            requirements.Alignment,
            GpuMemoryKind.DeviceLocal,
            requirements.Compatibility);
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
        return arena.Allocate(requirements.Size, requirements.Alignment, kind, requirements.Compatibility);
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
