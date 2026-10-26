using System.Runtime.InteropServices;
using Lumyte.Memory.DependencyInjection;

namespace Lumyte.Memory.Tests;

/// <summary>Tests memory ownership, budgets, reclamation and dependency injection.</summary>
public sealed class MemoryManagerTests
{
    private static readonly MemoryPoolId _cpu = new("cpu");

    /// <summary>A reservation atomically becomes one estimated charge and returns idempotently.</summary>
    /// <returns>Completion of the ownership checks.</returns>
    [Fact]
    public async Task ReservationCommitTracksActualUsageAsync()
    {
        await using MemoryManager manager = CreateManager(100, 120);
        await using IMemoryScope scope = manager.CreateScope("asset");
        using IMemoryReservation reservation = await scope.ReserveAsync(Request(80));
        Assert.Equal(80UL, manager.GetSnapshot(_cpu).ReservedBytes);

        using IMemoryCharge charge = reservation.Commit(new(110, MemoryAccuracy.Estimate));
        MemorySnapshot committed = manager.GetSnapshot(_cpu);
        Assert.Equal(0UL, committed.ReservedBytes);
        Assert.Equal(110UL, committed.CommittedBytes);
        Assert.Equal(110UL, committed.EstimatedBytes);
        Assert.False(committed.IsOverBudget);
        Assert.Throws<InvalidOperationException>(() => reservation.Commit(new(1, MemoryAccuracy.Exact)));

        charge.Dispose();
        charge.Dispose();
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>Cumulative releases remain observable when replacement allocations increase current usage.</summary>
    /// <returns>Completion of the release-counter checks.</returns>
    [Fact]
    public async Task SnapshotCountsReleasedChargesIndependentlyOfNewCommitsAsync()
    {
        await using MemoryManager manager = CreateManager(200, 200);
        await using IMemoryScope scope = manager.CreateScope("replacement accounting");
        using IMemoryReservation original = await scope.ReserveAsync(Request(40));
        using IMemoryCharge originalCharge = original.Commit(new(40, MemoryAccuracy.Exact));
        MemorySnapshot before = manager.GetSnapshot(_cpu);

        originalCharge.Dispose();
        using IMemoryReservation replacement = await scope.ReserveAsync(Request(80));
        using IMemoryCharge replacementCharge = replacement.Commit(new(80, MemoryAccuracy.Estimate));
        MemorySnapshot after = manager.GetSnapshot(_cpu);
        Assert.Equal(80UL, after.CommittedBytes);
        Assert.Equal(40UL, unchecked(after.ReleasedBytes - before.ReleasedBytes));

        replacementCharge.Dispose();
        replacementCharge.Dispose();
        using IMemoryReservation uncommitted = await scope.ReserveAsync(Request(20));
        uncommitted.Dispose();
        Assert.Equal(120UL, manager.GetSnapshot(_cpu).ReleasedBytes);
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>Returning a committed reservation preserves its charge until the charge or scope is disposed.</summary>
    /// <returns>Completion of the scope's ownership checks.</returns>
    [Fact]
    public async Task DisposedCommittedReservationKeepsChargeOwnedByScopeAsync()
    {
        await using MemoryManager manager = CreateManager(100, 100);
        await using IMemoryScope scope = manager.CreateScope("committed owner");
        using IMemoryReservation reservation = await scope.ReserveAsync(Request(40));
        using IMemoryCharge charge = reservation.Commit(new(60, MemoryAccuracy.Estimate));

        reservation.Dispose();
        reservation.Dispose();
        Assert.Equal(60UL, manager.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(60UL, manager.GetSnapshot(_cpu).EstimatedBytes);
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).ReservedBytes);

        await scope.DisposeAsync();
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).EstimatedBytes);
        charge.Dispose();
        charge.Dispose();
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>An overflowing commit leaves its reservation pending so callers can retry or return it.</summary>
    /// <param name="retryCommit">Whether to retry the commit instead of returning the pending reservation.</param>
    /// <returns>Completion of reservation recovery.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedCommitPreservesPendingReservationAsync(bool retryCommit)
    {
        await using MemoryManager manager = CreateManager(ulong.MaxValue, ulong.MaxValue);
        await using IMemoryScope scope = manager.CreateScope("overflow recovery");
        using IMemoryReservation original = await scope.ReserveAsync(Request(1));
        using IMemoryCharge originalCharge = original.Commit(new(1, MemoryAccuracy.Exact));
        using IMemoryReservation pending = await scope.ReserveAsync(Request(1));

        Assert.Throws<OverflowException>(() => pending.Commit(new(ulong.MaxValue, MemoryAccuracy.Estimate)));
        Assert.Equal(1UL, manager.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(1UL, manager.GetSnapshot(_cpu).ReservedBytes);
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).EstimatedBytes);

        if (retryCommit)
        {
            using IMemoryCharge retried = pending.Commit(new(2, MemoryAccuracy.Estimate));
            Assert.Equal(3UL, manager.GetSnapshot(_cpu).CommittedBytes);
            Assert.Equal(2UL, manager.GetSnapshot(_cpu).EstimatedBytes);
            Assert.Equal(0UL, manager.GetSnapshot(_cpu).ReservedBytes);
            pending.Dispose();
            Assert.Equal(3UL, manager.GetSnapshot(_cpu).CommittedBytes);
        }
        else
        {
            pending.Dispose();
        }

        Assert.Equal(1UL, manager.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).ReservedBytes);
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).EstimatedBytes);
        originalCharge.Dispose();
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
    }

    /// <summary>An underestimated external allocation remains charged while the pool rejects more work.</summary>
    /// <returns>Completion of the over-budget checks.</returns>
    [Fact]
    public async Task CommitOverHardLimitRemainsVisibleAsync()
    {
        await using MemoryManager manager = CreateManager(80, 100);
        await using IMemoryScope scope = manager.CreateScope("gpu epoch 1");
        using IMemoryReservation reservation = await scope.ReserveAsync(Request(90));
        using IMemoryCharge charge = reservation.Commit(new(130, MemoryAccuracy.Estimate));

        MemorySnapshot snapshot = manager.GetSnapshot(_cpu);
        Assert.True(snapshot.IsOverBudget);
        Assert.Equal(130UL, snapshot.CommittedBytes);
        await Assert.ThrowsAsync<MemoryBudgetExceededException>(async () => await scope.ReserveAsync(Request(1)));
    }

    /// <summary>Concurrent reservations cannot consume the same remaining hard-limit capacity.</summary>
    /// <returns>Completion of all competing reservations.</returns>
    [Fact]
    public async Task ConcurrentReservationsShareOneBudgetAsync()
    {
        await using MemoryManager manager = CreateManager(100, 100);
        await using IMemoryScope scope = manager.CreateScope("parallel");
        Task<IMemoryReservation>[] attempts = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () => await scope.ReserveAsync(Request(60))))
            .ToArray();
        await Task.WhenAll(attempts.Select(static async task =>
        {
            try
            {
                await task;
            }
            catch (MemoryBudgetExceededException)
            {
            }
        }));

        IMemoryReservation[] accepted = attempts.Where(static task => task.IsCompletedSuccessfully).Select(static task => task.Result).ToArray();
        Assert.Single(accepted);
        Assert.Equal(60UL, manager.GetSnapshot(_cpu).ReservedBytes);
        accepted[0].Dispose();
    }

    /// <summary>A buffer exposes its slice while the manager accounts for its backing chunk.</summary>
    /// <returns>Completion of buffer disposal.</returns>
    [Fact]
    public async Task RentTracksCapacityAndReturnsWithScopeAsync()
    {
        await using MemoryManager manager = CreateManager(4096, 4096);
        IMemoryScope scope = manager.CreateScope("decode");
        IMemoryBuffer<int> buffer = await scope.RentAsync<int>(_cpu, 17, MemoryPurpose.Streaming);

        Assert.Equal(17, buffer.Memory.Length);
        Assert.Equal(17, buffer.Capacity);
        Assert.Equal(checked((ulong)buffer.Capacity * sizeof(int)), buffer.ChargedBytes);
        Assert.True(manager.GetSnapshot(_cpu).CommittedBytes >= buffer.ChargedBytes);

        await scope.DisposeAsync();
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
        Assert.Throws<ObjectDisposedException>(() => buffer.Memory);
        buffer.Dispose();
    }

    /// <summary>A chunk falls back to the requested size when the preferred size exceeds the budget.</summary>
    /// <returns>Completion of the bounded rent.</returns>
    [Fact]
    public async Task RentFallsBackToRequestedCapacityWithinBudgetAsync()
    {
        await using MemoryManager manager = CreateManager(68, 68);
        await using IMemoryScope scope = manager.CreateScope("small");
        using IMemoryBuffer<int> buffer = await scope.RentAsync<int>(_cpu, 17, MemoryPurpose.Build);
        Assert.Equal(17, buffer.Capacity);
        Assert.Equal(68UL, buffer.ChargedBytes);
        Assert.Equal(68UL, manager.GetSnapshot(_cpu).CommittedBytes);
        buffer.Dispose();
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).ReservedBytes);
    }

    /// <summary>Reclamation reports ledger changes, continues after independent failures and blocks callback reservations.</summary>
    /// <returns>Completion of the reclamation request.</returns>
    [Fact]
    public async Task ReclaimUsesActualReleasedChargesAndAggregatesFailuresAsync()
    {
        await using MemoryManager manager = CreateManager(100, 100);
        await using IMemoryScope scope = manager.CreateScope("cache");
        IMemoryReservation reservation = await scope.ReserveAsync(Request(40));
        IMemoryCharge charge = reservation.Commit(new(40, MemoryAccuracy.Exact));
        DelegateReclaimer release = new(async (pool, _, cancellationToken) =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await scope.ReserveAsync(Request(1), cancellationToken));
            charge.Dispose();
            return 999;
        });
        DelegateReclaimer failure = new((_, _, _) => throw new InvalidOperationException("injected"));
        using IDisposable releaseRegistration = manager.RegisterReclaimer(release);
        using IDisposable failureRegistration = manager.RegisterReclaimer(failure);

        await Assert.ThrowsAsync<AggregateException>(async () => await manager.ReclaimAsync(_cpu, 100));
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(1, release.Calls);
        Assert.Equal(1, failure.Calls);
    }

    /// <summary>Reclamation counts released bytes even when an existing reservation commits during the callback.</summary>
    /// <returns>Completion of the reclamation request.</returns>
    [Fact]
    public async Task ReclaimTracksReleasesDespiteConcurrentCommitAsync()
    {
        await using MemoryManager manager = CreateManager(100, 100);
        await using IMemoryScope scope = manager.CreateScope("cache replacement");
        using IMemoryReservation original = await scope.ReserveAsync(Request(40));
        using IMemoryCharge originalCharge = original.Commit(new(40, MemoryAccuracy.Exact));
        using IMemoryReservation replacement = await scope.ReserveAsync(Request(40));
        using IDisposable registration = manager.RegisterReclaimer(new DelegateReclaimer((_, _, _) =>
        {
            replacement.Commit(new(40, MemoryAccuracy.Exact));
            originalCharge.Dispose();
            return ValueTask.FromResult(0UL);
        }));

        Assert.Equal(40UL, await manager.ReclaimAsync(_cpu, 40));
        Assert.Equal(40UL, manager.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).ReservedBytes);
    }

    /// <summary>Active and cancelled queued reclamations release their manager ownership before shutdown.</summary>
    /// <returns>Completion of both reclamation requests and manager shutdown.</returns>
    [Fact]
    public async Task CancelledQueuedReclaimReleasesManagerOwnershipAsync()
    {
        await using MemoryManager manager = CreateManager(100, 100);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable registration = manager.RegisterReclaimer(new DelegateReclaimer(async (_, _, _) =>
        {
            entered.SetResult();
            await release.Task;
            return 0;
        }));
        Task<ulong> active = manager.ReclaimAsync(_cpu, 1).AsTask();
        await entered.Task;
        registration.Dispose();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.DisposeAsync());
            using CancellationTokenSource cancellation = new();
            Task<ulong> queued = manager.ReclaimAsync(_cpu, 1, cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queued);
        }
        finally
        {
            release.TrySetResult();
            await active;
        }

        await manager.DisposeAsync();
    }

    /// <summary>The manager refuses shutdown without losing ownership and succeeds after every owner leaves.</summary>
    /// <returns>Completion of manager shutdown.</returns>
    [Fact]
    public async Task DisposeRequiresScopesAndRegistrationsToEndAsync()
    {
        MemoryManager manager = CreateManager(100, 100);
        IMemoryScope scope = manager.CreateScope("owner");
        IDisposable registration = manager.RegisterReclaimer(new DelegateReclaimer((_, _, _) => ValueTask.FromResult(0UL)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.DisposeAsync());
        await scope.DisposeAsync();
        registration.Dispose();
        await manager.DisposeAsync();
        await manager.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => manager.CreateScope("late"));
    }

    /// <summary>DI supplies one configured singleton and rejects ambiguous duplicate registration.</summary>
    /// <returns>Completion of provider shutdown.</returns>
    [Fact]
    public async Task DependencyInjectionRegistersConfiguredSingletonAsync()
    {
        ServiceCollection services = new();
        services.AddMemoryManagement(options => options.SetBudget(_cpu, 64, 128));
        Assert.Throws<InvalidOperationException>(() => services.AddMemoryManagement());
        await using ServiceProvider provider = services.BuildServiceProvider();
        IMemoryManager first = provider.GetRequiredService<IMemoryManager>();
        IMemoryManager second = provider.GetRequiredService<IMemoryManager>();
        Assert.Same(first, second);
        Assert.Equal(128UL, first.GetSnapshot(_cpu).HardLimitBytes);
    }

    /// <summary>DI forwards the configured chunk size to both allocation capacity and budget accounting.</summary>
    /// <returns>Completion of allocation and provider shutdown.</returns>
    [Fact]
    public async Task DependencyInjectionUsesConfiguredChunkCapacityAsync()
    {
        ServiceCollection services = new();
        services.AddMemoryManagement(options =>
        {
            options.ChunkSizeBytes = 96;
            options.SetBudget(_cpu, 1024, 1024);
        });
        await using ServiceProvider provider = services.BuildServiceProvider();
        IMemoryManager manager = provider.GetRequiredService<IMemoryManager>();
        await using IMemoryScope scope = manager.CreateScope("configured chunk");
        using IMemoryBuffer<byte> buffer = await scope.RentAsync<byte>(_cpu, 7, MemoryPurpose.Build);

        Assert.True(MemoryMarshal.TryGetArray<byte>(buffer.Memory, out ArraySegment<byte> segment));
        Assert.NotNull(segment.Array);
        Assert.Equal(96, segment.Array.Length);
        Assert.Equal(96UL, manager.GetSnapshot(_cpu).CommittedBytes);
        Assert.Equal(7, buffer.Capacity);
        Assert.Equal(7UL, buffer.ChargedBytes);
    }

    private static MemoryManager CreateManager(ulong softLimit, ulong hardLimit)
        => new(new Dictionary<MemoryPoolId, MemoryBudget> { [_cpu] = new(softLimit, hardLimit) });

    private static MemoryRequest Request(ulong bytes) => new() { Pool = _cpu, Bytes = bytes, Purpose = MemoryPurpose.Resident };

    private sealed class DelegateReclaimer(Func<MemoryPoolId, ulong, CancellationToken, ValueTask<ulong>> callback) : IMemoryReclaimer
    {
        internal int Calls { get; private set; }

        public ValueTask<ulong> ReclaimAsync(MemoryPoolId pool, ulong targetBytes, CancellationToken cancellationToken = default)
        {
            Calls++;
            return callback(pool, targetBytes, cancellationToken);
        }
    }
}
