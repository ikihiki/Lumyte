using System.Diagnostics;
using Lumyte.Execution;
using Lumyte.Memory;
using Lumyte.Resources;
using Lumyte.Resources.Execution;
using Xunit;

namespace Lumyte.Maps.Session.Tests;

/// <summary>Uses the real shared resource runtime and cooperative job pump, including actual generation cleanup.</summary>
public sealed class CooperativeSpatialTests
{
    /// <summary>Repeated movement cannot accumulate unused Manager generations or abandon cleanup jobs during stop.</summary>
    [Fact]
    public void RealCooperativeRuntimeReclaimsStagesAndDrainsBeforeStopped()
    {
        var runtime = new ResourceRuntime();
        var execution = new ExecutionRuntime([runtime]);
        execution.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(execution.StartAsync().AsTask());
        var memory = new MemoryManager();
        var stage = new Stage();
        var manager = new ResourceManager("cooperative-spatial", new Source(), [stage], memory, runtime);
        var map = new MapSession(manager, new Tiles(), new("test", "v1", new(0, 0, 0, 1), []), new() { MaxPendingTiles = 4, MaxResidentTiles = 2, MaxResidentCpuBytes = 512, MaxPrefetchTiles = 0, PrefetchRadius = 0, MaxFallbackDepth = 0 });
        try
        {
            map.SetViewport(new(100, 100));
            map.SetCamera(Camera(0, 0));
            map.Advance();
            Assert.Equal(0, stage.Builds);
            Assert.Equal(1, map.InFlightTiles);
            Assert.Equal(256, map.ResidentCpuBytes);
            for (int step = 0; step < 32; step++)
            {
                map.SetCamera(Camera(step % 4, (step / 4) % 4));
                Until(() => map.ReadyVisibleTiles == 1 && map.InFlightTiles == 0 && !map.IsMaintenanceInFlight);
                Assert.InRange(stage.Builds - stage.Releases, 1, 2);
                Assert.InRange(map.ResidentCpuBytes, 128, 512);
                Assert.InRange(map.PublishedTiles, 1, 2);
            }

            Assert.Equal(32, stage.Builds);
            Assert.Equal(30, stage.Releases);
            Assert.True(map.ReleasedGenerationCount > 0);
            map.BeginStop();
            map.Advance();
            Assert.False(map.IsStopped);
            Until(() => map.IsStopped);
            Assert.Equal(stage.Builds, stage.Releases);
            Assert.Equal(0, map.ResidentCpuBytes);
        }
        finally
        {
            Pump(map.DisposeAsync().AsTask());
            Pump(manager.DisposeAsync().AsTask());
            Pump(execution.StopAsync().AsTask());
            Pump(memory.DisposeAsync().AsTask());
        }

        void Until(Func<bool> done)
        {
            long start = Stopwatch.GetTimestamp();
            while (!done())
            {
                map.Advance();
                execution.PumpJobs(16, TimeSpan.FromMilliseconds(2));
                if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException("Cooperative spatial work did not reach its deterministic state.");
                }

                Thread.Sleep(1);
            }
        }

        void Pump(Task task)
        {
            long start = Stopwatch.GetTimestamp();
            while (!task.IsCompleted)
            {
                execution.PumpJobs(16, TimeSpan.FromMilliseconds(2));
                if (Stopwatch.GetElapsedTime(start) > TimeSpan.FromSeconds(10))
                {
                    throw new TimeoutException("Cooperative lifecycle operation did not terminate.");
                }

                Thread.Sleep(1);
            }

            task.GetAwaiter().GetResult();
        }
    }

    private static MapCamera Camera(int x, int y)
    {
        double worldY = (y + 0.5) / 4;
        return new(-135 + (90 * x), Math.Atan(Math.Sinh(Math.PI * (1 - (2 * worldY)))) * 180 / Math.PI, 2);
    }

    private sealed class Tiles() : MapTileSet("cooperative", "v1", "test", 2, 2, "handmade", new("cooperative", maxReservationBytesPerTile: 256))
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => new($"cooperative/{id.Zoom}/{id.X}/{id.Y}", variant: LoadProfile.Fingerprint);
    }

    private sealed class Stage : IResourceSourceStage<MapTileContent>
    {
        internal int Builds { get; private set; }

        internal int Releases { get; private set; }

        public ValueTask<MapTileContent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Builds++;
            return ValueTask.FromResult(new MapTileContent([], "v1", 128, 256));
        }

        public ValueTask ReleaseAsync(MapTileContent resource)
        {
            Releases++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Source : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("v1");
    }
}
