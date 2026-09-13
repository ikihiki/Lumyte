using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Lumyte.Graphics.Native.Resources;

/// <summary>Owns backing heaps and lends non-overlapping reserved byte ranges.</summary>
/// <remarks>
/// The backend is borrowed. Callers serialize arena operations, own all placed resources,
/// and finish their CPU/GPU uses before releasing slices. The arena performs no GPU wait.
/// </remarks>
public sealed class GpuMemoryArena : IDisposable
{
    private readonly INativeGpuBackend backend;
    private readonly ulong blockSize;
    private readonly Dictionary<CompatibilityKey, List<Block>> pools = [];
    private readonly Dictionary<GpuMemorySlice, Block> loans = new(ReferenceEqualityComparer.Instance);
    private bool disposed;

    public GpuMemoryArena(INativeGpuBackend backend, ulong blockSize)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (blockSize == 0) { throw new ArgumentOutOfRangeException(nameof(blockSize)); }
        this.backend = backend;
        this.blockSize = blockSize;
    }

    /// <summary>Lends reserved bytes for exactly this memory kind and opaque requirement token set.</summary>
    /// <remarks>Reuse the acquired requirement objects; equivalent-looking new tokens are not compared or combined.</remarks>
    public GpuMemorySlice Allocate(ulong size, ulong alignment, NativeGpuMemoryKind kind,
        ReadOnlySpan<NativeGpuMemoryCompatibility> compatibilities)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (size == 0) { throw new ArgumentOutOfRangeException(nameof(size)); }
        if (alignment == 0) { throw new ArgumentOutOfRangeException(nameof(alignment)); }
        CompatibilityKey key = new(kind, compatibilities);
        if (pools.TryGetValue(key, out List<Block>? blocks))
        {
            foreach (Block block in blocks)
            {
                if (block.Heap.Alignment < alignment || block.Heap.Alignment % alignment != 0) { continue; }
                if (TryAllocate(block, size, alignment) is { } slice) { return slice; }
            }
        }

        ulong capacity = AlignUp(Math.Max(blockSize, size), alignment);
        NativeGpuHeap heap = backend.CreateGpuHeap(capacity, alignment, kind, key.Tokens);
        try
        {
            Block block = new(heap, capacity);
            blocks ??= [];
            blocks.EnsureCapacity(checked(blocks.Count + 1));
            pools.EnsureCapacity(checked(pools.Count + 1));
            GpuMemorySlice slice = TryAllocate(block, size, alignment)!;
            blocks.Add(block);
            pools.TryAdd(key, blocks);
            return slice;
        }
        catch (Exception error)
        {
            try { backend.DestroyGpuHeap(heap); }
            catch (Exception cleanupError) { throw new AggregateException("Arena block initialization and cleanup failed.", error, cleanupError); }
            throw;
        }
    }

    /// <summary>Returns a loan after the caller has ended all uses and destroyed the placed resources.</summary>
    public void Release(GpuMemorySlice slice)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(slice);
        if (!ReferenceEquals(slice.Owner, this)) { throw new ArgumentException("The slice belongs to another arena.", nameof(slice)); }
        if (!loans.TryGetValue(slice, out Block? block)) { throw new InvalidOperationException("The slice has already been released."); }

        List<FreeRange> free = block.Free;
        free.EnsureCapacity(checked(free.Count + 1));
        int index = 0;
        while (index < free.Count && free[index].Offset < slice.Offset) { index++; }
        FreeRange returned = new(slice.Offset, slice.Size);
        if (index > 0 && free[index - 1].End == returned.Offset)
        {
            FreeRange before = free[index - 1];
            returned = new(before.Offset, checked(before.Size + returned.Size));
            free.RemoveAt(--index);
        }
        if (index < free.Count && returned.End == free[index].Offset)
        {
            returned = new(returned.Offset, checked(returned.Size + free[index].Size));
            free.RemoveAt(index);
        }
        free.Insert(index, returned);
        loans.Remove(slice);
        block.LoanCount--;
    }

    /// <summary>Detaches empty blocks and attempts every native heap release once, without waiting.</summary>
    /// <remarks>A failed native release has an ambiguous effect; its block is neither retried nor returned to the pool.</remarks>
    public void Trim()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        DestroyBlocks(DetachEmptyBlocks());
    }

    /// <summary>Destroys the arena only after every loan is returned. Rejection leaves the arena usable.</summary>
    public void Dispose()
    {
        if (disposed) { return; }
        if (loans.Count != 0) { throw new InvalidOperationException("Return every arena slice before disposal."); }
        DetachedBlocks empty = DetachEmptyBlocks();
        disposed = true;
        DestroyBlocks(empty);
    }

    private GpuMemorySlice? TryAllocate(Block block, ulong size, ulong alignment)
    {
        List<FreeRange> free = block.Free;
        for (int index = 0; index < free.Count; index++)
        {
            FreeRange range = free[index];
            ulong remainder = range.Offset % alignment;
            ulong padding = remainder == 0 ? 0 : alignment - remainder;
            if (padding > range.Size || size > range.Size - padding) { continue; }
            ulong offset = checked(range.Offset + padding);
            ulong end = checked(offset + size);
            GpuMemorySlice slice = new(this, block.Heap, offset, size);
            free.EnsureCapacity(checked(free.Count + 1));
            loans.EnsureCapacity(checked(loans.Count + 1));
            loans.Add(slice, block);
            free.RemoveAt(index);
            if (end < range.End) { free.Insert(index, new(end, range.End - end)); }
            if (padding != 0) { free.Insert(index, new(range.Offset, padding)); }
            block.LoanCount++;
            return slice;
        }
        return null;
    }

    private DetachedBlocks DetachEmptyBlocks()
    {
        KeyValuePair<CompatibilityKey, List<Block>>[] entries = pools.ToArray();
        Block[] empty = entries.SelectMany(static entry => entry.Value).Where(static block => block.LoanCount == 0).ToArray();
        // Prepare error storage while every heap is still owned by the pool. Recording
        // native cleanup failures must not allocate after ownership has been detached.
        Exception[] errors = new Exception[empty.Length];
        foreach (var entry in entries)
        {
            entry.Value.RemoveAll(static block => block.LoanCount == 0);
            if (entry.Value.Count == 0) { pools.Remove(entry.Key); }
        }
        return new(empty, errors);
    }

    private void DestroyBlocks(DetachedBlocks detached)
    {
        int errorCount = 0;
        foreach (Block block in detached.Blocks)
        {
            try { backend.DestroyGpuHeap(block.Heap); }
            catch (Exception error) { detached.Errors[errorCount++] = error; }
        }
        if (errorCount == 1) { ExceptionDispatchInfo.Capture(detached.Errors[0]).Throw(); }
        if (errorCount > 1) { throw new AggregateException("Releasing arena heaps failed.", detached.Errors.Take(errorCount)); }
    }

    private readonly record struct DetachedBlocks(Block[] Blocks, Exception[] Errors);

    private static ulong AlignUp(ulong value, ulong alignment)
    {
        ulong remainder = value % alignment;
        return remainder == 0 ? value : checked(value + (alignment - remainder));
    }

    private readonly record struct FreeRange(ulong Offset, ulong Size)
    {
        internal ulong End => checked(Offset + Size);
    }

    private sealed class Block(NativeGpuHeap heap, ulong capacity)
    {
        internal NativeGpuHeap Heap { get; } = heap;
        internal List<FreeRange> Free { get; } = [new(0, capacity)];
        internal int LoanCount;
    }

    private sealed class CompatibilityKey : IEquatable<CompatibilityKey>
    {
        private readonly NativeGpuMemoryKind kind;
        private readonly HashSet<NativeGpuMemoryCompatibility> tokens;
        private readonly int hash;

        internal CompatibilityKey(NativeGpuMemoryKind kind, ReadOnlySpan<NativeGpuMemoryCompatibility> compatibilities)
        {
            if (compatibilities.IsEmpty) { throw new ArgumentException("At least one compatibility token is required.", nameof(compatibilities)); }
            this.kind = kind;
            tokens = new(ReferenceEqualityComparer.Instance);
            int tokenHash = 0;
            foreach (NativeGpuMemoryCompatibility compatibility in compatibilities)
            {
                if (compatibility is null) { throw new ArgumentException("A compatibility token cannot be null.", nameof(compatibilities)); }
                if (tokens.Add(compatibility)) { tokenHash ^= RuntimeHelpers.GetHashCode(compatibility); }
            }
            Tokens = tokens.ToArray();
            hash = HashCode.Combine(kind, tokens.Count, tokenHash);
        }

        internal NativeGpuMemoryCompatibility[] Tokens { get; }
        public bool Equals(CompatibilityKey? other) => other is not null && kind == other.kind && tokens.SetEquals(other.tokens);
        public override bool Equals(object? obj) => obj is CompatibilityKey other && Equals(other);
        public override int GetHashCode() => hash;
    }
}
