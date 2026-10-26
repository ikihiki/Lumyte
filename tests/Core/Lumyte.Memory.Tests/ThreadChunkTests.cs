using System.Runtime.InteropServices;

namespace Lumyte.Memory.Tests;

/// <summary>Tests thread-owned CPU chunks, shared ownership and concurrent returns.</summary>
public sealed class ThreadChunkTests
{
    private static readonly MemoryPoolId _cpu = new("cpu");

    /// <summary>Live allocations on one thread share adjacent slices and one physical charge.</summary>
    /// <returns>Completion of the allocation thread.</returns>
    [Fact]
    public async Task SameThreadRentsShareChunkAsync()
    {
        await using MemoryManager manager = CreateManager(128, 128);
        await using IMemoryScope scope = manager.CreateScope("decode");
        await RunOnThreadAsync(() =>
        {
            using IMemoryBuffer<int> first = Rent<int>(scope, _cpu, 8);
            using IMemoryBuffer<int> second = Rent<int>(scope, _cpu, 4);
            ArraySegment<int> firstSegment = Segment(first);
            ArraySegment<int> secondSegment = Segment(second);

            Assert.Same(firstSegment.Array, secondSegment.Array);
            Assert.Equal(firstSegment.Offset + firstSegment.Count, secondSegment.Offset);
            Assert.Equal(8, first.Capacity);
            Assert.Equal(32UL, first.ChargedBytes);
            Assert.Equal(16UL, second.ChargedBytes);
            Assert.Equal(128UL, manager.GetSnapshot(_cpu).CommittedBytes);
            Assert.Equal(0UL, manager.GetSnapshot(_cpu).ReservedBytes);
            first.Memory.Span.Fill(17);
            second.Memory.Span.Fill(31);
            Assert.All(first.Memory.ToArray(), static value => Assert.Equal(17, value));
            Assert.All(second.Memory.ToArray(), static value => Assert.Equal(31, value));
        });
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>Different caller threads receive different chunks even when they share a scope.</summary>
    /// <returns>Completion of both caller threads.</returns>
    [Fact]
    public async Task DifferentThreadsUseDifferentChunksAsync()
    {
        await using MemoryManager manager = CreateManager(128, 64);
        await using IMemoryScope scope = manager.CreateScope("parallel");
        IMemoryBuffer<byte>? first = null;
        IMemoryBuffer<byte>? second = null;
        await RunOnThreadAsync(() => first = Rent<byte>(scope, _cpu, 1));
        await RunOnThreadAsync(() => second = Rent<byte>(scope, _cpu, 1));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(Segment(first).Array, Segment(second).Array);
        Assert.Equal(128UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>Chunks are isolated by manager, budget pool and element type on one thread.</summary>
    /// <returns>Completion of the allocation thread.</returns>
    [Fact]
    public async Task ManagerPoolAndElementTypeHaveSeparateChunksAsync()
    {
        MemoryPoolId staging = new("staging");
        await using MemoryManager firstManager = CreateManager(1024, 64);
        await using MemoryManager secondManager = CreateManager(1024, 64);
        await using IMemoryScope firstScope = firstManager.CreateScope("first");
        await using IMemoryScope secondScope = secondManager.CreateScope("second");
        await RunOnThreadAsync(() =>
        {
            using IMemoryBuffer<byte> first = Rent<byte>(firstScope, _cpu, 1);
            using IMemoryBuffer<byte> otherPool = Rent<byte>(firstScope, staging, 1);
            using IMemoryBuffer<byte> otherManager = Rent<byte>(secondScope, _cpu, 1);
            using IMemoryBuffer<int> otherType = Rent<int>(firstScope, _cpu, 1);

            Assert.NotSame(Segment(first).Array, Segment(otherPool).Array);
            Assert.NotSame(Segment(first).Array, Segment(otherManager).Array);
            Assert.Equal(128UL, firstManager.GetSnapshot(_cpu).CommittedBytes);
            Assert.Equal(64UL, firstManager.GetSnapshot(staging).CommittedBytes);
            Assert.Equal(64UL, secondManager.GetSnapshot(_cpu).CommittedBytes);
        });
    }

    /// <summary>Disposing one scope leaves another scope's slice of the shared chunk alive.</summary>
    /// <returns>Completion of both scopes.</returns>
    [Fact]
    public async Task SharedChunkLivesUntilLastScopeReturnsAsync()
    {
        await using MemoryManager manager = CreateManager(64, 64);
        await using IMemoryScope firstScope = manager.CreateScope("first");
        await using IMemoryScope secondScope = manager.CreateScope("second");
        IMemoryBuffer<int>? first = null;
        IMemoryBuffer<int>? second = null;
        await RunOnThreadAsync(() =>
        {
            first = Rent<int>(firstScope, _cpu, 4);
            second = Rent<int>(secondScope, _cpu, 4);
        });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Same(Segment(first).Array, Segment(second).Array);
        await firstScope.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => first.Memory);
        second.Memory.Span.Fill(42);
        Assert.All(second.Memory.ToArray(), static value => Assert.Equal(42, value));
        Assert.Equal(64UL, manager.GetSnapshot(_cpu).CommittedBytes);
        await secondScope.DisposeAsync();
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>A return from another thread cannot rewind the owner thread's allocation cursor.</summary>
    /// <returns>Completion of the owner and return threads.</returns>
    [Fact]
    public async Task RemoteReturnDoesNotReusePartiallyReleasedChunkAsync()
    {
        await using MemoryManager manager = CreateManager(128, 64);
        await using IMemoryScope scope = manager.CreateScope("remote return");
        await RunOnThreadAsync(() =>
        {
            using IMemoryBuffer<int> first = Rent<int>(scope, _cpu, 4);
            using IMemoryBuffer<int> second = Rent<int>(scope, _cpu, 4);
            ArraySegment<int> firstSegment = Segment(first);
            (Thread returning, Task returned) = StartThread(first.Dispose);
            returning.Join();
            Assert.True(returned.IsCompletedSuccessfully, returned.Exception?.ToString());

            using IMemoryBuffer<int> third = Rent<int>(scope, _cpu, 8);
            ArraySegment<int> thirdSegment = Segment(third);
            Assert.Same(firstSegment.Array, thirdSegment.Array);
            Assert.Equal(8, thirdSegment.Offset);
            second.Memory.Span.Fill(23);
            third.Memory.Span.Fill(47);
            Assert.All(second.Memory.ToArray(), static value => Assert.Equal(23, value));
            using IMemoryBuffer<int> rollover = Rent<int>(scope, _cpu, 1);
            Assert.NotSame(thirdSegment.Array, Segment(rollover).Array);
            Assert.Equal(128UL, manager.GetSnapshot(_cpu).CommittedBytes);
        });
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>A full chunk retains its charge while a rollover that exceeds the budget fails.</summary>
    /// <returns>Completion of the allocation thread.</returns>
    [Fact]
    public async Task RolloverCannotExceedHardBudgetAsync()
    {
        await using MemoryManager manager = CreateManager(64, 64);
        await using IMemoryScope scope = manager.CreateScope("bounded");
        await RunOnThreadAsync(() =>
        {
            using IMemoryBuffer<byte> first = Rent<byte>(scope, _cpu, 48);
            Assert.Throws<MemoryBudgetExceededException>(() => Rent<byte>(scope, _cpu, 32));
            using IMemoryBuffer<byte> remaining = Rent<byte>(scope, _cpu, 16);
            Assert.Same(Segment(first).Array, Segment(remaining).Array);
            Assert.Equal(48, Segment(remaining).Offset);
            first.Memory.Span.Fill(53);
            remaining.Memory.Span.Fill(71);
            Assert.All(first.Memory.ToArray(), static value => Assert.Equal((byte)53, value));
            Assert.Equal(64UL, manager.GetSnapshot(_cpu).CommittedBytes);
            Assert.Equal(0UL, manager.GetSnapshot(_cpu).ReservedBytes);
        });
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>A request larger than the preferred chunk is charged for its full requested capacity.</summary>
    /// <returns>Completion of the oversized allocation.</returns>
    [Fact]
    public async Task OversizedRentUsesRequestedCapacityAsync()
    {
        await using MemoryManager manager = CreateManager(100, 32);
        await using IMemoryScope scope = manager.CreateScope("oversized");
        using IMemoryBuffer<int> buffer = await scope.RentAsync<int>(_cpu, 20, MemoryPurpose.Streaming);
        Assert.Equal(20, buffer.Capacity);
        Assert.Equal(20, Segment(buffer).Array!.Length);
        Assert.Equal(80UL, buffer.ChargedBytes);
        Assert.Equal(80UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>The last return releases a chunk immediately even when its owner thread remains alive.</summary>
    /// <returns>Completion of the owner thread.</returns>
    [Fact]
    public async Task EmptyChunkIsReleasedBeforeNextRentAsync()
    {
        await using MemoryManager manager = CreateManager(64, 64);
        await using IMemoryScope scope = manager.CreateScope("no idle retention");
        await RunOnThreadAsync(() =>
        {
            using IMemoryBuffer<byte> first = Rent<byte>(scope, _cpu, 1);
            byte[]? previous = Segment(first).Array;
            first.Dispose();
            Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
            using IMemoryBuffer<byte> next = Rent<byte>(scope, _cpu, 1);
            Assert.NotSame(previous, Segment(next).Array);
            Assert.Equal(64UL, manager.GetSnapshot(_cpu).CommittedBytes);
        });
    }

    /// <summary>Concurrent duplicate returns release each slice exactly once across thread boundaries.</summary>
    /// <returns>Completion of all returning threads.</returns>
    [Fact]
    public async Task ConcurrentRemoteReturnsReleaseChunkOnceAsync()
    {
        await using MemoryManager manager = CreateManager(64, 64);
        await using IMemoryScope scope = manager.CreateScope("returns");
        IMemoryBuffer<byte>? first = null;
        IMemoryBuffer<byte>? second = null;
        await RunOnThreadAsync(() =>
        {
            first = Rent<byte>(scope, _cpu, 1);
            second = Rent<byte>(scope, _cpu, 1);
        });

        Assert.NotNull(first);
        Assert.NotNull(second);
        using ManualResetEventSlim start = new(false);
        Task[] returns = Enumerable.Range(0, 8).Select(_ => RunOnThreadAsync(() =>
        {
            start.Wait();
            first.Dispose();
            first.Dispose();
        })).ToArray();
        start.Set();
        await Task.WhenAll(returns);
        Assert.Equal(64UL, manager.GetSnapshot(_cpu).CommittedBytes);
        await RunOnThreadAsync(second.Dispose);
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>A final remote return racing another rent cannot retire the new slice's chunk.</summary>
    /// <returns>Completion of the owner and return threads.</returns>
    [Fact]
    public async Task LastRemoteReturnCanRaceOwnerRentAsync()
    {
        await using MemoryManager manager = CreateManager(128, 64);
        await using IMemoryScope scope = manager.CreateScope("retirement race");
        await RunOnThreadAsync(() =>
        {
            for (int iteration = 0; iteration < 64; iteration++)
            {
                using IMemoryBuffer<byte> previous = Rent<byte>(scope, _cpu, 1);
                (Thread returning, Task returned) = StartThread(previous.Dispose);
                using IMemoryBuffer<byte> next = Rent<byte>(scope, _cpu, 1);
                returning.Join();
                Assert.True(returned.IsCompletedSuccessfully, returned.Exception?.ToString());

                next.Memory.Span[0] = 73;
                Assert.Equal((byte)73, next.Memory.Span[0]);
                Assert.Equal(64UL, manager.GetSnapshot(_cpu).CommittedBytes);
                next.Dispose();
                Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
            }
        });
    }

    /// <summary>Competing threads cannot reserve chunks beyond the common pool budget.</summary>
    /// <returns>Completion of allocation and return threads.</returns>
    [Fact]
    public async Task ConcurrentThreadChunksRespectHardBudgetAsync()
    {
        await using MemoryManager manager = CreateManager(128, 64);
        await using IMemoryScope scope = manager.CreateScope("budget contention");
        using ManualResetEventSlim start = new(false);
        var buffers = new IMemoryBuffer<byte>?[8];
        Task[] rentals = Enumerable.Range(0, buffers.Length).Select(index => RunOnThreadAsync(() =>
        {
            start.Wait();
            try
            {
                buffers[index] = Rent<byte>(scope, _cpu, 64);
            }
            catch (MemoryBudgetExceededException)
            {
            }
        })).ToArray();
        start.Set();
        await Task.WhenAll(rentals);

        Assert.Equal(2, buffers.Count(static buffer => buffer is not null));
        Assert.Equal(128UL, manager.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).ReservedBytes);
        await Task.WhenAll(buffers.OfType<IMemoryBuffer<byte>>().Select(buffer => RunOnThreadAsync(buffer.Dispose)));
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>Closing a scope during concurrent rents drains every admitted chunk.</summary>
    /// <returns>Completion of the scope close races.</returns>
    [Fact]
    public async Task ConcurrentScopeCloseAndRentLeavesNoChargesAsync()
    {
        await using MemoryManager manager = CreateManager(4096, 64);
        for (int iteration = 0; iteration < 32; iteration++)
        {
            await using IMemoryScope scope = manager.CreateScope("closing");
            using ManualResetEventSlim start = new(false);
            Task[] rentals = Enumerable.Range(0, 4).Select(_ => RunOnThreadAsync(() =>
            {
                start.Wait();
                try
                {
                    Rent<byte>(scope, _cpu, 1);
                }
                catch (ObjectDisposedException)
                {
                }
            })).ToArray();
            var disposal = Task.Run(async () =>
            {
                start.Wait();
                await scope.DisposeAsync();
            });
            start.Set();
            await Task.WhenAll(rentals.Append(disposal));
            await scope.DisposeAsync();
            Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
            Assert.Equal(0UL, manager.GetSnapshot(_cpu).ReservedBytes);
        }
    }

    private static MemoryManager CreateManager(ulong hardLimitBytes, int chunkSizeBytes)
        => new(new Dictionary<MemoryPoolId, MemoryBudget> { [_cpu] = new(hardLimitBytes, hardLimitBytes) }, chunkSizeBytes);

    private static IMemoryBuffer<T> Rent<T>(IMemoryScope scope, MemoryPoolId pool, int length)
        where T : unmanaged
    {
        ValueTask<IMemoryBuffer<T>> rental = scope.RentAsync<T>(pool, length, MemoryPurpose.Build);
        Assert.True(rental.IsCompleted);
        return rental.Result;
    }

    private static ArraySegment<T> Segment<T>(IMemoryBuffer<T> buffer)
        where T : unmanaged
    {
        Assert.True(MemoryMarshal.TryGetArray<T>(buffer.Memory, out ArraySegment<T> segment));
        return segment;
    }

    private static Task RunOnThreadAsync(Action action) => StartThread(action).Completion;

    private static (Thread Thread, Task Completion) StartThread(Action action)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();
        return (thread, completion.Task);
    }
}
