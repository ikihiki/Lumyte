namespace Lumyte.Graphics.Native.Resources.Tests.Unit.Utilities;

public sealed class GpuMemoryArenaLifetimeTests
{
    [Fact]
    public void AForeignArenaCannotReleaseALoan()
    {
        using TestBackend backend = new();
        using GpuMemoryArena owner = new(backend, 16);
        using GpuMemoryArena other = new(backend, 16);
        GpuMemorySlice slice = owner.Allocate(8, 8, NativeGpuMemoryKind.GpuOnly, [new TestBackend.Compatibility()]);

        ArgumentException error = Assert.Throws<ArgumentException>(() => other.Release(slice));

        Assert.Equal("slice", error.ParamName);
        owner.Release(slice);
    }

    [Fact]
    public void ReusingAnOffsetDoesNotReactivateTheOldLoan()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 16);
        TestBackend.Compatibility token = new();
        GpuMemorySlice old = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        arena.Release(old);
        GpuMemorySlice current = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => arena.Release(old));

        Assert.Contains("already been released", error.Message);
        Assert.NotSame(old, current);
        Assert.Equal((old.Heap, old.Offset, old.Size), (current.Heap, current.Offset, current.Size));
        arena.Release(current);
    }

    [Fact]
    public void FailedNativeCreationDoesNotChangeExistingLoans()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 16);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        InvalidOperationException expected = new("Allocation rejected.");
        backend.CreationError = expected;

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
            arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]));
        backend.CreationError = null;
        arena.Release(first);
        GpuMemorySlice replacement = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.Same(expected, actual);
        Assert.Same(first.Heap, replacement.Heap);
        Assert.Single(backend.Creations);
        Assert.Empty(backend.Destroyed);
        arena.Release(replacement);
    }

    [Fact]
    public void TrimDestroysOnlyCompletelyEmptyBlocks()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 16);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(8, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice second = arena.Allocate(8, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice otherBlock = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        arena.Release(first);
        arena.Release(otherBlock);

        arena.Trim();

        Assert.Same(otherBlock.Heap, Assert.Single(backend.Destroyed));
        GpuMemorySlice reused = arena.Allocate(8, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        Assert.Same(second.Heap, reused.Heap);
        arena.Release(second);
        arena.Release(reused);
    }

    [Fact]
    public void TrimDetachesAllEmptyBlocksAndNeverRetriesFailedDestruction()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 16);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice second = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        arena.Release(first);
        arena.Release(second);
        InvalidOperationException expected = new("Release effect unknown.");
        backend.DestructionError = heap => ReferenceEquals(heap, first.Heap) ? expected : null;

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() => arena.Trim());
        arena.Trim();
        GpuMemorySlice replacement = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);

        Assert.Same(expected, actual);
        Assert.Equal(new[] { first.Heap, second.Heap }, backend.Destroyed);
        Assert.NotSame(first.Heap, replacement.Heap);
        Assert.NotSame(second.Heap, replacement.Heap);
        arena.Release(replacement);
    }

    [Fact]
    public void MultipleReleaseErrorsArePreservedAfterEveryAttempt()
    {
        using TestBackend backend = new();
        using GpuMemoryArena arena = new(backend, 16);
        TestBackend.Compatibility token = new();
        GpuMemorySlice first = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice second = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        arena.Release(first);
        arena.Release(second);
        Exception firstError = new InvalidOperationException("First release failed.");
        Exception secondError = new InvalidOperationException("Second release failed.");
        backend.DestructionError = heap => ReferenceEquals(heap, first.Heap) ? firstError : secondError;

        AggregateException error = Assert.Throws<AggregateException>(() => arena.Trim());

        Assert.Collection(error.InnerExceptions,
            actual => Assert.Same(firstError, actual), actual => Assert.Same(secondError, actual));
        Assert.Equal(new[] { first.Heap, second.Heap }, backend.Destroyed);
    }

    [Fact]
    public void DisposalWithOutstandingLoansLeavesEveryBlockUnchanged()
    {
        using TestBackend backend = new();
        GpuMemoryArena arena = new(backend, 16);
        TestBackend.Compatibility token = new();
        GpuMemorySlice empty = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        GpuMemorySlice live = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        arena.Release(empty);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => arena.Dispose());

        Assert.Contains("Return every arena slice", error.Message);
        Assert.Empty(backend.Destroyed);
        GpuMemorySlice reused = arena.Allocate(16, 8, NativeGpuMemoryKind.GpuOnly, [token]);
        Assert.Same(empty.Heap, reused.Heap);
        arena.Release(live);
        arena.Release(reused);
        arena.Dispose();
        Assert.Equal(2, backend.Destroyed.Count);
        Assert.False(backend.Disposed);
    }

    [Fact]
    public void DisposeAttemptsNativeReleaseOnceEvenWhenItFails()
    {
        using TestBackend backend = new();
        GpuMemoryArena arena = new(backend, 16);
        GpuMemorySlice slice = arena.Allocate(8, 8, NativeGpuMemoryKind.GpuOnly, [new TestBackend.Compatibility()]);
        arena.Release(slice);
        InvalidOperationException expected = new("Release failed.");
        backend.DestructionError = _ => expected;

        InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() => arena.Dispose());
        arena.Dispose();

        Assert.Same(expected, actual);
        Assert.Same(slice.Heap, Assert.Single(backend.Destroyed));
        Assert.False(backend.Disposed);
        Assert.Throws<ObjectDisposedException>(() => arena.Allocate(8, 8, NativeGpuMemoryKind.GpuOnly, [new TestBackend.Compatibility()]));
    }
}
