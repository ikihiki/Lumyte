using System.Collections.Concurrent;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Resources;
using Xunit;

namespace Lumyte.Maps.Session.Tests;

/// <summary>Deterministic spatial admission, completion and actual resource retirement checks.</summary>
public sealed class SpatialSessionTests
{
    /// <summary>Visible demand precedes prefetch and equal-distance IDs have a stable order.</summary>
    /// <returns>The admission checks.</returns>
    [Fact]
    public async Task VisibleThenDistanceThenStableIdAdmissionAsync()
    {
        await using var fixture = new Fixture();
        await using MapSession map = fixture.Create(Options() with { MaxInFlightTiles = 1, MaxCompletedTiles = 1, PrefetchRadius = 1, MaxPrefetchTiles = 4 });
        map.SetCamera(new(0, 0, 2));
        map.SetViewport(new(100, 100));
        MapTileId[] visible = [new(2, 1, 1), new(2, 1, 2), new(2, 2, 1), new(2, 2, 2)];
        for (int index = 0; index < visible.Length; index++)
        {
            await PumpAsync(map, () => fixture.Stage.Started.Count == index + 1);
            Assert.Equal(fixture.Tiles.GetKey(visible[index]).Address, fixture.Stage.Started[index]);
            fixture.Stage.Complete(fixture.Stage.Started[index]);
            await PumpAsync(map, () => map.PublishedTiles == index + 1);
        }

        Assert.All(fixture.Stage.Started.Take(4), address => Assert.Contains(address, visible.Select(id => fixture.Tiles.GetKey(id).Address)));
        fixture.Stage.CompleteAll();
    }

    /// <summary>Updates before Advance coalesce, and distance outranks stable ID.</summary>
    /// <returns>The coalescing and distance checks.</returns>
    [Fact]
    public async Task CameraUpdatesCoalesceBeforeDispatchAsync()
    {
        await using var fixture = new Fixture();
        await using MapSession map = fixture.Create(Options() with { MaxInFlightTiles = 1, MaxCompletedTiles = 1 });
        map.SetViewport(new(600, 600));
        map.SetCamera(CameraAt(0.5, 0.5));
        map.SetCamera(CameraAt(3.5, 0.5));
        map.SetCamera(CameraAt(1.75, 2.25));
        Assert.Empty(fixture.Stage.Started);
        Assert.Equal(0, map.InFlightTiles);
        await PumpAsync(map, () => fixture.Stage.Started.Count == 1);
        Assert.Equal(fixture.Tiles.GetKey(new(2, 1, 2)).Address, fixture.Stage.Started[0]);
        fixture.Stage.CompleteAll();
    }

    /// <summary>Huge demand is rejected as a whole and remains bounded until a smaller view arrives.</summary>
    /// <returns>The overflow recovery checks.</returns>
    [Fact]
    public async Task PendingOverflowNeverDispatchesPartialVisibleSelectionAsync()
    {
        await using var fixture = new Fixture();
        await using MapSession map = fixture.Create(Options() with { MaxPendingTiles = 2 });
        map.SetCamera(new(0, 0, 2));
        map.SetViewport(new(float.MaxValue, float.MaxValue));
        map.Advance();
        Assert.True(map.IsDemandOverflow);
        Assert.Empty(map.VisibleTiles);
        Assert.Equal(0, map.PendingTiles);
        Assert.Equal(0, map.InFlightTiles);
        Assert.Empty(fixture.Stage.Started);
        map.SetCamera(CameraAt(0.5, 0.5));
        map.SetViewport(new(100, 100));
        await PumpAsync(map, () => fixture.Stage.Started.Count == 1);
        Assert.False(map.IsDemandOverflow);
        fixture.Stage.CompleteAll();
    }

    /// <summary>Reverse completion has no head-of-line blocking and publication remains bounded.</summary>
    /// <returns>The completion-slot checks.</returns>
    [Fact]
    public async Task ReverseCompletionHonorsPublicationAndCompletionCapsAsync()
    {
        await using var fixture = new Fixture();
        await using MapSession map = fixture.Create(Options() with { MaxInFlightTiles = 8, MaxCompletedTiles = 2, MaxPublicationsPerAdvance = 1 });
        map.SetCamera(new(0, 0, 2));
        map.SetViewport(new(100, 100));
        await PumpAsync(map, () => fixture.Stage.Started.Count == 2);
        Assert.Equal(2, map.InFlightTiles);
        Assert.Equal(512, map.ResidentCpuBytes);
        fixture.Stage.Complete(fixture.Stage.Started[1]);
        await PumpAsync(map, () => map.PublishedTiles == 1);
        Assert.False(fixture.Stage.IsCompleted(fixture.Stage.Started[0]));
        Assert.InRange(map.InFlightTiles, 1, 2);
        fixture.Stage.CompleteAll();
        await PumpAsync(map, () => map.PublishedTiles == 4);
        Assert.Equal(512, map.ResidentCpuBytes);
        Assert.Empty(map.Failures);
    }

    /// <summary>A camera round trip can reuse an accepted result without another build.</summary>
    /// <returns>The same-content result reuse checks.</returns>
    [Fact]
    public async Task OldCameraResultIsReusableWhenCurrentDemandReturnsAsync()
    {
        await using var fixture = new Fixture();
        await using MapSession map = fixture.Create(Options() with { MaxCompletedTiles = 1 });
        map.SetViewport(new(100, 100));
        map.SetCamera(CameraAt(0.5, 0.5));
        await PumpAsync(map, () => fixture.Stage.Started.Count == 1);
        ResourceAddress original = fixture.Stage.Started[0];
        map.SetCamera(CameraAt(2.5, 0.5));
        map.Advance();
        Assert.Equal(256, map.ResidentCpuBytes);
        map.SetCamera(CameraAt(0.5, 0.5));
        fixture.Stage.Complete(original);
        await PumpAsync(map, () => map.PublishedTiles == 1);
        Assert.Single(fixture.Stage.Started);
        Assert.Equal(128, map.ResidentCpuBytes);
    }

    /// <summary>Version changes discard obsolete completion and validate the new manifest revision.</summary>
    /// <returns>The content identity and revision checks.</returns>
    [Fact]
    public async Task ContentSwitchRejectsObsoleteAndUnexpectedRevisionAsync()
    {
        await using var fixture = new Fixture();
        await using MapSession map = fixture.Create(Options() with { MaxCompletedTiles = 1 });
        map.SetViewport(new(100, 100));
        map.SetCamera(CameraAt(0.5, 0.5));
        await PumpAsync(map, () => fixture.Stage.Started.Count == 1);
        var replacement = new Tiles("v2", "expected-v2");
        map.SetTileSet(replacement);
        fixture.Stage.Complete(fixture.Stage.Started[0]);
        await PumpAsync(map, () => fixture.Stage.Started.Count == 2);
        Assert.Equal(0, map.PublishedTiles);
        Assert.Single(fixture.Stage.Releases);
        Assert.Equal(replacement.GetKey(new(2, 0, 0)).Address, fixture.Stage.Started[1]);
        fixture.Stage.Complete(fixture.Stage.Started[1], "wrong-revision");
        await PumpAsync(map, () => map.Failures.Count != 0);
        Assert.IsType<InvalidDataException>(Assert.Single(map.Failures));
        Assert.Equal(0, map.PublishedTiles);
        await PumpAsync(map, () => !map.IsMaintenanceInFlight && map.ResidentCpuBytes == 0);
        Assert.Equal(2, fixture.Stage.Releases.Count);
    }

    /// <summary>A matching explicit manifest revision admits the completed tile.</summary>
    /// <returns>The expected revision success check.</returns>
    [Fact]
    public async Task MatchingExpectedSourceRevisionPublishesAsync()
    {
        await using var fixture = new Fixture();
        await using MapSession map = fixture.Create(Options());
        map.SetTileSet(new Tiles("v1", "v1"));
        map.SetCamera(CameraAt(0.5, 0.5));
        map.SetViewport(new(100, 100));
        fixture.Stage.CompleteAll();
        await PumpAsync(map, () => map.PublishedTiles == 1);
        Assert.Empty(map.Failures);
        Assert.Equal(128, map.ResidentCpuBytes);
        Assert.Single(fixture.Stage.Started);
    }

    /// <summary>Stop cannot refund an ignored-cancellation build before actual terminal collection.</summary>
    /// <returns>The stop and shared disposal checks.</returns>
    [Fact]
    public async Task StopOwnsIgnoredCancellationUntilTerminalAndSharesDisposeAsync()
    {
        await using var fixture = new Fixture();
        MapSession map = fixture.Create(Options() with { MaxCompletedTiles = 1 });
        map.SetCamera(CameraAt(0.5, 0.5));
        map.SetViewport(new(100, 100));
        await PumpAsync(map, () => fixture.Stage.Started.Count == 1);
        map.BeginStop();
        map.Advance();
        Assert.False(map.IsStopped);
        Assert.Equal(256, map.ResidentCpuBytes);
        int notifications = 0;
        map.Changed += () => notifications++;
        Task first = map.DisposeAsync().AsTask();
        Task second = map.DisposeAsync().AsTask();
        Assert.Same(first, second);
        Assert.False(first.IsCompleted);
        fixture.Stage.CompleteAll();
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(map.IsStopped);
        Assert.Equal(0, map.ResidentCpuBytes);
        Assert.Equal(0, map.PublishedTiles);
        Assert.Equal(0, notifications);
        Assert.Single(fixture.Stage.Releases);
        Assert.Throws<ObjectDisposedException>(map.Advance);
    }

    /// <summary>LRU eviction keeps its charge and blocks replacement until actual stage release finishes.</summary>
    /// <returns>The session-owned trim and low-capacity checks.</returns>
    [Fact]
    public async Task LruWaitsForActualCleanupBeforeReusingLowCapacityAsync()
    {
        await using var fixture = new Fixture();
        await using MapSession map = fixture.Create(Options() with { MaxResidentTiles = 2, MaxResidentCpuBytes = 512 });
        map.SetViewport(new(100, 100));
        map.SetCamera(CameraAt(0.5, 0.5));
        fixture.Stage.CompleteAll();
        await PumpAsync(map, () => map.PublishedTiles == 1);
        ResourceAddress oldest = fixture.Stage.Started[0];
        map.SetCamera(CameraAt(1.5, 0.5));
        await PumpAsync(map, () => map.PublishedTiles == 2);
        map.SetCamera(CameraAt(0.5, 0.5));
        map.Advance();
        fixture.Stage.HoldReleases = true;
        map.SetCamera(CameraAt(2.5, 0.5));
        await PumpAsync(map, () => fixture.Stage.ReleaseAttempts.Count == 1);
        Assert.Equal(fixture.Stage.Started[1], fixture.Stage.ReleaseAttempts[0]);
        Assert.DoesNotContain(oldest, fixture.Stage.ReleaseAttempts);
        Assert.Empty(fixture.Stage.Releases);
        Assert.Equal(2, fixture.Stage.Started.Count);
        Assert.Equal(1, map.RetiringTiles);
        Assert.Equal(256, map.ResidentCpuBytes);
        Assert.True(map.IsMaintenanceInFlight);
        Assert.True(map.IsCapacityBlocked);
        fixture.Stage.AllowReleases();
        await PumpAsync(map, () => fixture.Stage.Started.Count == 3 && map.PublishedTiles == 2);
        Assert.Single(fixture.Stage.Releases);
        Assert.Equal(256, map.ResidentCpuBytes);
    }

    /// <summary>Shared manager cache hits are independently charged and independently pinned.</summary>
    /// <returns>The pin-sharing checks.</returns>
    [Fact]
    public async Task TwoSessionsKeepIndependentPinsUntilBothReturnAsync()
    {
        await using var fixture = new Fixture();
        await using MapSession first = fixture.Create(Options());
        await using MapSession second = fixture.Create(Options());
        fixture.Stage.CompleteAll();
        foreach (MapSession map in new[] { first, second })
        {
            map.SetCamera(CameraAt(0.5, 0.5));
            map.SetViewport(new(100, 100));
            await PumpAsync(map, () => map.PublishedTiles == 1);
            Assert.Equal(128, map.ResidentCpuBytes);
        }

        Assert.Single(fixture.Stage.Started);
        await first.DisposeAsync();
        Assert.Empty(fixture.Stage.Releases);
        Assert.Equal(1, second.PublishedTiles);
        Assert.Equal(128, second.ResidentCpuBytes);
        await second.DisposeAsync();
        Assert.Single(fixture.Stage.Releases);
    }

    /// <summary>Queue rejection returns admission capacity and is retried without a diagnostic failure.</summary>
    /// <returns>The retry checks.</returns>
    [Fact]
    public async Task QueueFullRetriesWithSingleSlotBudgetsAsync()
    {
        await using var fixture = new Fixture();
        fixture.Runtime.RejectOnce = true;
        fixture.Stage.CompleteAll();
        await using MapSession map = fixture.Create(Options() with { MaxPendingTiles = 1, MaxResidentTiles = 1, MaxInFlightTiles = 1, MaxCompletedTiles = 1, MaxResidentCpuBytes = 256 });
        map.SetCamera(CameraAt(0.5, 0.5));
        map.SetViewport(new(100, 100));
        await PumpAsync(map, () => map.PublishedTiles == 1);
        Assert.False(fixture.Runtime.RejectOnce);
        Assert.Empty(map.Failures);
        Assert.Single(fixture.Stage.Started);
        Assert.Equal(128, map.ResidentCpuBytes);
    }

    /// <summary>A cleanup failure keeps the retirement charged, and Dispose is retryable.</summary>
    /// <returns>The cleanup failure checks.</returns>
    [Fact]
    public async Task CleanupThrowOncePreservesChargeAndDisposeRetriesAsync()
    {
        await using var fixture = new Fixture();
        MapSession map = fixture.Create(Options());
        fixture.Stage.CompleteAll();
        map.SetCamera(CameraAt(0.5, 0.5));
        map.SetViewport(new(100, 100));
        await PumpAsync(map, () => map.PublishedTiles == 1);
        fixture.Stage.ThrowReleaseOnce = true;
        await Assert.ThrowsAnyAsync<Exception>(() => map.DisposeAsync().AsTask());
        Assert.False(map.IsStopped);
        Assert.NotNull(map.MaintenanceFailure);
        Assert.Equal(128, map.ResidentCpuBytes);
        Assert.Equal(1, map.RetiringTiles);
        Assert.Empty(fixture.Stage.Releases);
        await map.DisposeAsync();
        Assert.True(map.IsStopped);
        Assert.Equal(0, map.ResidentCpuBytes);
        Assert.Equal(2, fixture.Stage.ReleaseAttempts.Count);
        Assert.Single(fixture.Stage.Releases);
    }

    /// <summary>A completion retired after a trim started cannot refund its reservation using the earlier sweep's acknowledgment.</summary>
    /// <returns>The maintenance-batch ownership checks.</returns>
    [Fact]
    public async Task LateRetirementKeepsItsChargeForItsOwnMaintenanceBatchAsync()
    {
        await using var fixture = new Fixture();
        await using MapSession map = fixture.Create(Options() with { MaxResidentTiles = 2, MaxResidentCpuBytes = 512 });
        using var releaseOnExit = new StageCompletion(fixture.Stage);
        map.SetViewport(new(100, 100));
        for (int x = 0; x < 2; x++)
        {
            map.SetCamera(CameraAt(x + 0.5, 0.5));
            await PumpAsync(map, () => fixture.Stage.Started.Count == x + 1);
            fixture.Stage.Complete(fixture.Stage.Started[x]);
            await PumpAsync(map, () => map.PublishedTiles == x + 1);
        }

        fixture.Stage.HoldReleases = true;
        map.SetCamera(CameraAt(2.5, 0.5));
        await PumpAsync(map, () => fixture.Stage.ReleaseAttempts.Count == 1);
        Assert.Equal(1, map.RetiringTiles);

        // The second old generation becomes obsolete only after the first sweep captured its batch.
        map.SetTileSet(new Tiles("v2"));
        map.Advance();
        Assert.Equal(2, map.RetiringTiles);
        Assert.Equal(256, map.ResidentCpuBytes);
        fixture.Stage.AllowReleases();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (map.RetiringTiles == 2)
        {
            map.Advance();
            await Task.Delay(1, deadline.Token);
        }

        Assert.Equal(1, map.RetiringTiles);
        Assert.Equal(128, map.ResidentCpuBytes);
        Assert.Equal(2, fixture.Stage.Started.Count);
        fixture.Stage.CompleteAll();
        await PumpAsync(map, () => fixture.Stage.Started.Count == 3 && map.ReadyVisibleTiles == 1);
        Assert.Equal(2, fixture.Stage.Releases.Count);
    }

    /// <summary>Another session's accepted build can pin publication snapshots; all Maps admission pauses until terminal collection and real cleanup.</summary>
    /// <returns>The shared-manager build-view barrier checks.</returns>
    [Fact]
    public async Task SharedMaintenanceWaitsForAcceptedBuildViewsAndBlocksNewAdmissionsAsync()
    {
        await using var fixture = new Fixture();
        MapSessionOptions options = Options() with { MaxResidentTiles = 1, MaxResidentCpuBytes = 256 };
        await using MapSession first = fixture.Create(options);
        await using MapSession second = fixture.Create(options);
        using var finishOnExit = new StageCompletion(fixture.Stage);
        first.SetViewport(new(100, 100));
        first.SetCamera(CameraAt(0.5, 0.5));
        await PumpAsync(first, () => fixture.Stage.Started.Count == 1);
        fixture.Stage.Complete(fixture.Stage.Started[0]);
        await PumpAsync(first, () => first.ReadyVisibleTiles == 1);
        second.SetViewport(new(100, 100));
        second.SetCamera(CameraAt(1.5, 0.5));
        await PumpAsync(second, () => fixture.Stage.Started.Count == 2);
        first.SetCamera(CameraAt(2.5, 0.5));
        first.Advance();
        Assert.Equal(1, first.RetiringTiles);
        Assert.Equal(128, first.ResidentCpuBytes);
        Assert.False(first.IsMaintenanceInFlight);
        Assert.Empty(fixture.Stage.ReleaseAttempts);
        second.SetCamera(CameraAt(3.5, 0.5));
        for (int turn = 0; turn < 8; turn++)
        {
            first.Advance();
            second.Advance();
            Assert.Equal(2, fixture.Stage.Started.Count);
            Assert.Equal(128, first.ResidentCpuBytes);
            Assert.Equal(256, second.ResidentCpuBytes);
        }

        fixture.Stage.CompleteAll();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (first.ReadyVisibleTiles != 1 || second.ReadyVisibleTiles != 1)
        {
            first.Advance();
            second.Advance();
            await Task.Delay(1, deadline.Token);
        }

        Assert.Equal(4, fixture.Stage.Started.Count);
        Assert.Equal(2, fixture.Stage.Releases.Count);
        Assert.Equal(128, first.ResidentCpuBytes);
        Assert.Equal(128, second.ResidentCpuBytes);
    }

    /// <summary>Truncating fallback demand keeps the nearest ancestor rather than the first normalized ID.</summary>
    /// <returns>The bounded fallback candidate priority check.</returns>
    [Fact]
    public async Task FallbackAdmissionCapKeepsNearestAncestorAsync()
    {
        await using var fixture = new Fixture();
        await using MapSession map = fixture.Create(Options() with { MaxPendingTiles = 5, MaxInFlightTiles = 5, MaxCompletedTiles = 5, MaxFallbackDepth = 1 });
        using var finishOnExit = new StageCompletion(fixture.Stage);
        var tiles = new Tiles(minZoom: 1);
        map.SetTileSet(tiles);
        map.SetCamera(CameraAt(2.1, 2.1));
        map.SetViewport(new(256, 256));
        await PumpAsync(map, () => fixture.Stage.Started.Count == 5);
        Assert.Equal(tiles.GetKey(new(1, 1, 1)).Address, fixture.Stage.Started[4]);
        Assert.All(fixture.Stage.Started.Take(4), address => Assert.Contains("/2/", address.Id.Value!));
        fixture.Stage.CompleteAll();
        await PumpAsync(map, () => map.ReadyVisibleTiles == 4 && map.InFlightTiles == 0);
    }

    /// <summary>The last fixed or finite owner performs manager cleanup even after a spatial owner returned its shared pin.</summary>
    /// <param name="finite">Whether the legacy session selects from its finite universe.</param>
    /// <returns>The final-owner cache cleanup checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FixedAndFiniteLastOwnerDisposalReclaimsSharedAndDistinctGenerationsAsync(bool finite)
    {
        await using var fixture = new Fixture();
        fixture.Stage.CompleteAll();
        await using MapSession spatial = fixture.Create(Options());
        spatial.SetCamera(CameraAt(0.5, 0.5));
        spatial.SetViewport(new(100, 100));
        await PumpAsync(spatial, () => spatial.ReadyVisibleTiles == 1);
        for (int x = 0; x < 4; x++)
        {
            await using var legacy = new MapSession(fixture.Manager, fixture.Tiles, new("test", "v1", new(0, 0, 0, 1), []), Options() with { SelectVisibleTiles = finite }, [new(2, x, 0)]);
            legacy.SetCamera(CameraAt(x + 0.5, 0.5));
            legacy.SetViewport(new(100, 100));
            await PumpAsync(legacy, () => legacy.PublishedTiles == 1);
            if (x == 0)
            {
                await spatial.DisposeAsync();
                Assert.Empty(fixture.Stage.Releases);
            }

            await legacy.DisposeAsync();
            Assert.Equal(x + 1, fixture.Stage.Releases.Count);
        }

        Assert.Equal(4, fixture.Stage.Started.Count);
    }

    /// <summary>Fixed and finite retains also hold the manager build-view barrier until their real completion is collected.</summary>
    /// <param name="finite">Whether the old session selects its finite universe by viewport.</param>
    /// <returns>The mixed-mode admission and cleanup checks.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FixedAndFiniteInFlightParticipateInSpatialMaintenanceBarrierAsync(bool finite)
    {
        await using var fixture = new Fixture();
        await using MapSession spatial = fixture.Create(Options() with { MaxResidentTiles = 1, MaxResidentCpuBytes = 256 });
        await using var legacy = new MapSession(fixture.Manager, fixture.Tiles, new("test", "v1", new(0, 0, 0, 1), []), Options() with { MaxInFlightTiles = 1, MaxCompletedTiles = 1, SelectVisibleTiles = finite }, [new(2, 1, 0), new(2, 3, 0)]);
        using var finishOnExit = new StageCompletion(fixture.Stage);
        spatial.SetViewport(new(100, 100));
        spatial.SetCamera(CameraAt(0.5, 0.5));
        await PumpAsync(spatial, () => fixture.Stage.Started.Count == 1);
        fixture.Stage.Complete(fixture.Stage.Started[0]);
        await PumpAsync(spatial, () => spatial.ReadyVisibleTiles == 1);
        legacy.SetViewport(new(100, 100));
        legacy.SetCamera(CameraAt(1.5, 0.5));
        await PumpAsync(legacy, () => fixture.Stage.Started.Count == 2);
        spatial.SetCamera(CameraAt(2.5, 0.5));
        spatial.Advance();
        Assert.Equal(1, spatial.RetiringTiles);
        Assert.Equal(128, spatial.ResidentCpuBytes);
        Assert.False(spatial.IsMaintenanceInFlight);
        Assert.Empty(fixture.Stage.ReleaseAttempts);
        legacy.SetCamera(CameraAt(3.5, 0.5));
        fixture.Stage.Complete(fixture.Stage.Started[1]);
        await PumpAsync(legacy, () => legacy.PublishedTiles == 1);
        Assert.Equal(2, fixture.Stage.Started.Count);
        fixture.Stage.CompleteAll();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (spatial.ReadyVisibleTiles != 1 || legacy.ReadyVisibleTiles != 1)
        {
            spatial.Advance();
            legacy.Advance();
            await Task.Delay(1, deadline.Token);
        }

        Assert.Equal(4, fixture.Stage.Started.Count);
        Assert.Single(fixture.Stage.Releases);
        await legacy.DisposeAsync();
        Assert.Equal(3, fixture.Stage.Releases.Count);
        Assert.Equal(1, spatial.ReadyVisibleTiles);
        await spatial.DisposeAsync();
        Assert.Equal(4, fixture.Stage.Releases.Count);
    }

    /// <summary>Starting Dispose early is not a substitute for observing accepted retains reach terminal collection.</summary>
    /// <param name="mode">Fixed, finite, or spatial mode.</param>
    /// <returns>The early-disposal terminal ownership checks.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EarlyDisposeDoesNotClaimStoppedWhileAcceptedRetainIsPendingAsync(int mode)
    {
        await using var fixture = new Fixture();
        MapSessionOptions options = Options() with { SelectVisibleTiles = mode == 1 };
        await using MapSession map = mode == 2 ? fixture.Create(options)
            : new(fixture.Manager, fixture.Tiles, new("test", "v1", new(0, 0, 0, 1), []), options, [new(2, 0, 0)]);
        using var finishOnExit = new StageCompletion(fixture.Stage);
        map.SetViewport(new(100, 100));
        map.SetCamera(CameraAt(0.5, 0.5));
        await PumpAsync(map, () => fixture.Stage.Started.Count == 1);
        Task disposal = map.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        Assert.False(map.IsStopped);
        Assert.Equal(1, map.InFlightTiles);
        Assert.Equal(256, map.ResidentCpuBytes);
        fixture.Stage.CompleteAll();
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(map.IsStopped);
        Assert.Equal(0, map.ResidentCpuBytes);
        Assert.Single(fixture.Stage.Releases);
    }

    private static MapSessionOptions Options() => new()
    {
        MaxPendingTiles = 16,
        MaxResidentTiles = 16,
        MaxInFlightTiles = 4,
        MaxCompletedTiles = 4,
        MaxPublicationsPerAdvance = 1,
        MaxResidentCpuBytes = 4096,
        MaxPrefetchTiles = 0,
        PrefetchRadius = 0,
        MaxFallbackDepth = 0,
    };

    private static MapCamera CameraAt(double x, double y) => new((x / 4 * 360) - 180, Math.Atan(Math.Sinh(Math.PI * (1 - (2 * y / 4)))) * 180 / Math.PI, 2);

    private static async Task PumpAsync(MapSession map, Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            deadline.Token.ThrowIfCancellationRequested();
            int before = map.PublishedTiles;
            map.Advance();
            Assert.InRange(map.PublishedTiles - before, -16, 1);
            await Task.Delay(1, deadline.Token);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly MemoryManager _memory = new();

        internal Fixture()
        {
            Manager = new("spatial-tests", new Source(), [Stage], _memory, Runtime);
        }

        internal Stage Stage { get; } = new();

        internal Runtime Runtime { get; } = new();

        internal Tiles Tiles { get; } = new();

        internal ResourceManager Manager { get; }

        public async ValueTask DisposeAsync()
        {
            Stage.CompleteAll();
            Stage.AllowReleases();
            await Manager.TrimAsync();
            await Manager.DisposeAsync();
            await _memory.DisposeAsync();
        }

        internal MapSession Create(MapSessionOptions options) => new(Manager, Tiles, new("test", "v1", new(0, 0, 0, 1), []), options);
    }

    private sealed class Tiles(string version = "v1", string? expectedRevision = null, int minZoom = 2) : MapTileSet("dataset", version, "test", minZoom, 2, "handmade", new("spatial-test", maxReservationBytesPerTile: 256))
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => new($"{Id}/{ContentVersion}/{id.Zoom}/{id.X}/{id.Y}", variant: LoadProfile.Fingerprint);

        public override string? GetExpectedSourceRevision(MapTileId id) => expectedRevision;
    }

    private sealed class Source : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("v1");
    }

    private sealed class Stage : IResourceSourceStage<MapTileContent>
    {
        private readonly ConcurrentDictionary<ResourceAddress, TaskCompletionSource<MapTileContent>> _gates = new();
        private readonly ConcurrentDictionary<MapTileContent, ResourceAddress> _addresses = new(ReferenceEqualityComparer.Instance);
        private readonly ConcurrentQueue<ResourceAddress> _started = new();
        private readonly ConcurrentQueue<ResourceAddress> _releaseAttempts = new();
        private readonly ConcurrentQueue<ResourceAddress> _releases = new();
        private readonly TaskCompletionSource _releaseGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _completeAll;

        internal IReadOnlyList<ResourceAddress> Started => _started.ToArray();

        internal IReadOnlyList<ResourceAddress> ReleaseAttempts => _releaseAttempts.ToArray();

        internal IReadOnlyList<ResourceAddress> Releases => _releases.ToArray();

        internal bool HoldReleases { get; set; }

        internal bool ThrowReleaseOnce { get; set; }

        public async ValueTask<MapTileContent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            ResourceAddress address = context.Address;
            TaskCompletionSource<MapTileContent> gate = Gate(address);
            _started.Enqueue(address);
            if (_completeAll)
            {
                Complete(address);
            }

            // Intentionally ignore cancellation: accepted work must remain terminally owned.
            return await gate.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async ValueTask ReleaseAsync(MapTileContent resource)
        {
            ResourceAddress address = _addresses[resource];
            _releaseAttempts.Enqueue(address);
            if (HoldReleases)
            {
                await _releaseGate.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }

            if (ThrowReleaseOnce)
            {
                ThrowReleaseOnce = false;
                throw new InvalidOperationException("one cleanup failure");
            }

            _releases.Enqueue(address);
        }

        internal bool IsCompleted(ResourceAddress address) => Gate(address).Task.IsCompleted;

        internal void Complete(ResourceAddress address, string revision = "v1")
        {
            var content = new MapTileContent([], revision, 128, 256);
            _addresses.TryAdd(content, address);
            Gate(address).TrySetResult(content);
        }

        internal void CompleteAll()
        {
            _completeAll = true;
            foreach (ResourceAddress address in _gates.Keys)
            {
                Complete(address);
            }
        }

        internal void AllowReleases()
        {
            HoldReleases = false;
            _releaseGate.TrySetResult();
        }

        private TaskCompletionSource<MapTileContent> Gate(ResourceAddress address) => _gates.GetOrAdd(address, static _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private sealed class StageCompletion(Stage stage) : IDisposable
    {
        public void Dispose()
        {
            stage.CompleteAll();
            stage.AllowReleases();
        }
    }

    private sealed class Runtime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        internal bool RejectOnce { get; set; }

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            if (RejectOnce)
            {
                RejectOnce = false;
                return ValueTask.FromException(new ExecutionQueueFullException("deterministic queue rejection"));
            }

            return context.ExecuteAsync(cancellationToken);
        }

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }
}
