using Lumyte.Memory.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

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

    /// <summary>A pooled buffer exposes the requested length and charges its actual capacity.</summary>
    /// <returns>Completion of buffer disposal.</returns>
    [Fact]
    public async Task RentTracksCapacityAndReturnsWithScopeAsync()
    {
        await using MemoryManager manager = CreateManager(4096, 4096);
        IMemoryScope scope = manager.CreateScope("decode");
        IMemoryBuffer<int> buffer = await scope.RentAsync<int>(_cpu, 17, MemoryPurpose.Streaming);

        Assert.Equal(17, buffer.Memory.Length);
        Assert.True(buffer.Capacity >= 17);
        Assert.Equal(checked((ulong)buffer.Capacity * sizeof(int)), buffer.ChargedBytes);
        Assert.Equal(buffer.ChargedBytes, manager.GetSnapshot(_cpu).CommittedBytes);

        await scope.DisposeAsync();
        Assert.Equal(0UL, manager.GetSnapshot(_cpu).CommittedBytes);
        Assert.Throws<ObjectDisposedException>(() => buffer.Memory);
        buffer.Dispose();
    }

    /// <summary>A CPU rent whose actual pool capacity exceeds the hard limit is not published or charged.</summary>
    /// <returns>Completion of the rejected rent.</returns>
    [Fact]
    public async Task RentRejectsActualCapacityOverBudgetAsync()
    {
        await using MemoryManager manager = CreateManager(68, 68);
        await using IMemoryScope scope = manager.CreateScope("small");
        await Assert.ThrowsAsync<MemoryBudgetExceededException>(async () => await scope.RentAsync<int>(_cpu, 17, MemoryPurpose.Build));
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
