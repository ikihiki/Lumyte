namespace Lumyte.Graphics.Native.Resources.Tests.Unit.Utilities;

public sealed class GpuMemoryArenaTests
{
    [Fact]
    public void AllocationAlignsOffsetAndPreservesReservedSize()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 64);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(9, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice second = arena.Allocate(8, 8, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.Equal((0ul, 9ul, 16ul, 8ul), (first.Offset, first.Size, second.Offset, second.Size));
        Assert.Same(first.Heap, second.Heap);
        Assert.Single(backend.Creations);
        arena.Release(first);
        arena.Release(second);
    }

    [Fact]
    public void AlignmentPaddingRemainsAvailableForSmallerLoans()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 32);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(9, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice second = arena.Allocate(8, 8, NativeGpuMemoryKind.GpuOnly, [token]);

        GpuMemorySlice padding = arena.Allocate(7, 1, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.Equal((9ul, 7ul), (padding.Offset, padding.Size));
        Assert.Same(first.Heap, padding.Heap);
        arena.Release(second);
        arena.Release(first);
        arena.Release(padding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdjacentReturnedRangesCoalesceRegardlessOfReleaseOrder(bool reverse)
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 32);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice second = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);

        arena.Release(reverse ? second : first);
        arena.Release(reverse ? first : second);
        GpuMemorySlice merged = arena.Allocate(32, 8, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.Equal(0ul, merged.Offset);
        Assert.Same(first.Heap, merged.Heap);
        Assert.Single(backend.Creations);
        arena.Release(merged);
    }

    [Fact]
    public void LiveMiddleLoanPreventsOverlappingReuse()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 48);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice middle = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice last = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        arena.Release(first);
        arena.Release(last);

        GpuMemorySlice large = arena.Allocate(32, 8, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.NotSame(middle.Heap, large.Heap);
        arena.Release(middle);
        arena.Release(large);
    }

    [Fact]
    public void OpaqueTokenSetIgnoresOrderAndDuplicates()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 64);
        TestBackend.Compatibility firstToken = new(), secondToken = new();
        GpuMemorySlice first = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [firstToken, secondToken]);

        GpuMemorySlice second = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [secondToken, firstToken, secondToken]);

        Assert.Same(first.Heap, second.Heap);
        TestBackend.Creation creation = Assert.Single(backend.Creations);
        Assert.Equal(2, creation.Compatibilities.Length);
        Assert.Contains(creation.Compatibilities, value => ReferenceEquals(value, firstToken));
        Assert.Contains(creation.Compatibilities, value => ReferenceEquals(value, secondToken));
        arena.Release(first);
        arena.Release(second);
    }

    [Fact]
    public void DistinctOpaqueTokensAreNotInferredEquivalent()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 64);
        GpuMemorySlice first = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [new TestBackend.Compatibility()]);

        GpuMemorySlice second = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [new TestBackend.Compatibility()]);

        Assert.NotSame(first.Heap, second.Heap);
        arena.Release(first);
        arena.Release(second);
    }

    [Fact]
    public void ASubsetDoesNotReuseTheSupersetPool()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 64);
        TestBackend.Compatibility firstToken = new(), secondToken = new();
        GpuMemorySlice mixed = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [firstToken, secondToken]);

        GpuMemorySlice single = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [firstToken]);

        Assert.NotSame(mixed.Heap, single.Heap);
        arena.Release(mixed);
        arena.Release(single);
    }

    [Fact]
    public void MemoryKindSeparatesPools()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 64);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(16, 8, NativeGpuMemoryKind.CpuVisible, [token]);

        GpuMemorySlice second = arena.Allocate(16, 8, NativeGpuMemoryKind.Readback, [token]);

        Assert.NotSame(first.Heap, second.Heap);
        Assert.Equal(NativeGpuMemoryKind.Readback, second.Heap.Kind);
        arena.Release(first);
        arena.Release(second);
    }

    [Fact]
    public void TokenInputArrayIsSnapshottedForLaterBlockCreation()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 16);
        TestBackend.Compatibility token = new();
        NativeGpuMemoryCompatibility[] input = [token];
        GpuMemorySlice first = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, input);
        input[0] = new TestBackend.Compatibility();

        GpuMemorySlice second = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.All(backend.Creations, creation => Assert.Same(token, Assert.Single(creation.Compatibilities)));
        arena.Release(first);
        arena.Release(second);
    }

    [Fact]
    public void LargerAlignmentRequiresASuitableHeapBase()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 64);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(8, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        arena.Release(first);

        GpuMemorySlice stronger = arena.Allocate(8, 32, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.NotSame(first.Heap, stronger.Heap);
        Assert.Equal(32ul, stronger.Heap.Alignment);
        arena.Release(stronger);
    }

    [Fact]
    public void SufficientHeapBaseAlignmentCanServeSmallerAlignment()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 64);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(8, 32, NativeGpuMemoryKind.GpuOnly, [token]);

        GpuMemorySlice second = arena.Allocate(8, 8, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.Same(first.Heap, second.Heap);
        Assert.Equal(8ul, second.Offset);
        arena.Release(first);
        arena.Release(second);
    }

    [Fact]
    public void HeapBaseMustBeAnAlignmentMultiple()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 24);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(1, 6, NativeGpuMemoryKind.GpuOnly, [token]);

        GpuMemorySlice second = arena.Allocate(1, 4, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.NotSame(first.Heap, second.Heap);
        arena.Release(first);
        arena.Release(second);
    }

    [Fact]
    public void LargerThanBlockLoanUsesOneAlignedAllocation()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 16);

        GpuMemorySlice slice = arena.Allocate(35, 16, NativeGpuMemoryKind.GpuOnly, [new TestBackend.Compatibility()]);

        Assert.Equal((48ul, 16ul, 35ul), (Assert.Single(backend.Creations).Heap.Size, slice.Heap.Alignment, slice.Size));
        arena.Release(slice);
    }

    [Fact]
    public void AlignmentNearUlongLimitDoesNotOverflowAnIntermediateSum()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, ulong.MaxValue - 1);

        GpuMemorySlice slice = arena.Allocate(1, 3, NativeGpuMemoryKind.GpuOnly, [new TestBackend.Compatibility()]);

        Assert.Equal(ulong.MaxValue, slice.Heap.Size);
        arena.Release(slice);
    }

    [Fact]
    public void UnrepresentableBlockCapacityFailsBeforeNativeAllocation()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, ulong.MaxValue);

        Assert.Throws<OverflowException>(() => arena.Allocate(1, 2, NativeGpuMemoryKind.GpuOnly, [new TestBackend.Compatibility()]));

        Assert.Empty(backend.Creations);
    }

    [Theory]
    [InlineData(0ul, 1ul, "size")]
    [InlineData(1ul, 0ul, "alignment")]
    public void EmptyAllocationInputsFailBeforeNativeAllocation(ulong size, ulong alignment, string parameter)
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 16);

        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            arena.Allocate(size, alignment, NativeGpuMemoryKind.GpuOnly, [new TestBackend.Compatibility()]));

        Assert.Equal(parameter, error.ParamName);
        Assert.Empty(backend.Creations);
    }

    [Fact]
    public void AllocationRequiresAtLeastOneOpaqueToken()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 16);

        ArgumentException error = Assert.Throws<ArgumentException>(() => arena.Allocate(1, 1, NativeGpuMemoryKind.GpuOnly, []));

        Assert.Equal("compatibilities", error.ParamName);
        Assert.Empty(backend.Creations);
    }

    [Fact]
    public void FragmentedLoansRemainDisjointAndReturnTheWholeBlock()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 4096);
        TestBackend.Compatibility token = new();
        GpuMemorySlice seed = arena.Allocate(1, 120, NativeGpuMemoryKind.GpuOnly, [token]);
        NativeGpuHeap heap = seed.Heap;
        arena.Release(seed);
        List<GpuMemorySlice> live = [];
        ulong[] alignments = [1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20, 24, 30, 40];
        Random random = new(1701);

        for (int operation = 0; operation < 512; operation++)
        {
            if (live.Count != 0 && (live.Count == 32 || random.Next(3) == 0))
            {
                int index = random.Next(live.Count);
                arena.Release(live[index]);
                live.RemoveAt(index);
                continue;
            }

            ulong alignment = alignments[random.Next(alignments.Length)];
            GpuMemorySlice slice = arena.Allocate((ulong)random.Next(1, 32), alignment, NativeGpuMemoryKind.GpuOnly, [token]);
            Assert.Same(heap, slice.Heap);
            Assert.Equal(0ul, slice.Offset % alignment);
            Assert.All(live, other => Assert.False(
                slice.Offset < other.Offset + other.Size && other.Offset < slice.Offset + slice.Size,
                $"Loan [{slice.Offset}, {slice.Offset + slice.Size}) overlaps [{other.Offset}, {other.Offset + other.Size})."));
            live.Add(slice);
        }
        foreach (GpuMemorySlice slice in live) { arena.Release(slice); }
        GpuMemorySlice whole = arena.Allocate(heap.Size, 120, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.Equal((heap, 0ul, heap.Size), (whole.Heap, whole.Offset, whole.Size));
        Assert.Single(backend.Creations);
        arena.Release(whole);
    }

    [Fact]
    public void FinalByteCanBeAllocatedAndReturnedWithoutOverflow()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, ulong.MaxValue);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(ulong.MaxValue - 2, 1, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice middle = arena.Allocate(1, 1, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice last = arena.Allocate(1, 1, NativeGpuMemoryKind.GpuOnly, [token]);

        arena.Release(first);
        arena.Release(last);
        arena.Release(middle);
        GpuMemorySlice whole = arena.Allocate(ulong.MaxValue, 1, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.Equal((ulong.MaxValue - 2, ulong.MaxValue - 1), (middle.Offset, last.Offset));
        Assert.Equal((first.Heap, 0ul, ulong.MaxValue), (whole.Heap, whole.Offset, whole.Size));
        Assert.Single(backend.Creations);
        arena.Release(whole);
    }

    [Fact]
    public void NullCompatibilityCannotReachTheBackend()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 16);

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            arena.Allocate(1, 1, NativeGpuMemoryKind.GpuOnly, [new TestBackend.Compatibility(), null!]));

        Assert.Equal("compatibilities", error.ParamName);
        Assert.Empty(backend.Creations);
    }
}
