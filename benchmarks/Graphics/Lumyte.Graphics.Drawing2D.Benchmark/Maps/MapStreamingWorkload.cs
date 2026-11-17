using Lumyte.Maps;
using Lumyte.Maps.Mvt;
using Lumyte.Memory;
using Lumyte.Resources;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Maps;

/// <summary>Controlled real M4 arrival, publication, LRU retirement and shared-manager trim, without network input.</summary>
public static class MapStreamingWorkload
{
    /// <summary>Runs an A-B-A spatial cycle with one resident tile and explicitly gated real decode/release stages.</summary>
    /// <returns>Deterministic executed operation counts, never elapsed time.</returns>
    public static async Task<StreamingObservation> RunAsync()
    {
        await using var memory = new MemoryManager();
        var first = new MapTileId(1, 0, 1);
        var second = new MapTileId(1, 1, 1);
        byte[] bytes = Lumyte.Benchmarks.Maps.DenseMapFixture.CreateBytes();
        var registration = new InMemoryMvtRegistration("m6-stream", "1", Lumyte.Benchmarks.Maps.DenseMapFixture.Profile, new Dictionary<MapTileId, ReadOnlyMemory<byte>> { [first] = bytes, [second] = bytes });
        using var stage = new ControlledStage(new MvtResourceStage(registration));
        await using var manager = new ResourceManager("m6-stream", new InMemoryMvtSource(registration), [stage], memory, new Runtime());
        await using var session = new MapSession(manager, new Tiles(registration), Lumyte.Benchmarks.Maps.DenseMapFixture.Style(), new()
        {
            MaxResidentTiles = 1,
            MaxPendingTiles = 2,
            MaxInFlightTiles = 1,
            MaxCompletedTiles = 1,
            PrefetchRadius = 0,
            MaxPrefetchTiles = 0,
            MaxFallbackDepth = 0,
            MaxPublicationsPerAdvance = 1,
        });
        session.SetViewport(new(128, 128));
        int publications = 0;
        int heldRetirements = 0;
        try
        {
            foreach (double longitude in new[] { -90.0, 90.0, -90.0 })
            {
                session.SetCamera(new(longitude, -66.51326044311186, 1));
                session.Advance();
                if (publications != 0)
                {
                    await AdvanceUntilAsync(session, () => session.RetiringTiles == 1 && session.IsMaintenanceInFlight);
                    if (session.ResidentCpuBytes <= 0 || session.ReadyVisibleTiles != 0 || stage.Releases != publications - 1)
                    {
                        throw new InvalidOperationException("M4 returned retirement capacity before actual release completion.");
                    }

                    heldRetirements++;
                    stage.ReleasePermit.Release();
                    await AdvanceUntilAsync(session, () => session.RetiringTiles == 0 && !session.IsMaintenanceInFlight && session.InFlightTiles == 1);
                }

                if (session.ReadyVisibleTiles != 0 || session.InFlightTiles != 1)
                {
                    throw new InvalidOperationException("Controlled arrival must remain unpublished before its real decode gate opens.");
                }

                stage.BuildPermit.Release();
                await AdvanceUntilAsync(session, () => session.ReadyVisibleTiles == 1);
                publications++;
                if (session.PublishedTiles != 1 || session.InFlightTiles != 0 || session.RetiringTiles != 0)
                {
                    throw new InvalidOperationException("Spatial publication exceeded the one-tile resident contract.");
                }
            }

            return new(stage.Builds, publications, heldRetirements, stage.Releases, session.ReleasedGenerationCount);
        }
        finally
        {
            // Keep failure cleanup terminally owned, even if the assertion fails while a stage is blocked.
            stage.BuildPermit.Release(8);
            stage.ReleasePermit.Release(8);
            await session.DisposeAsync();
        }
    }

    private static async Task AdvanceUntilAsync(MapSession session, Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate())
        {
            deadline.Token.ThrowIfCancellationRequested();
            session.Advance();
            if (session.MaintenanceFailure is { } failure)
            {
                throw failure;
            }

            if (session.Failures.Count != 0)
            {
                throw session.Failures[0];
            }

            await Task.Yield();
        }
    }

    private sealed class Tiles(InMemoryMvtRegistration registration) : MapTileSet("m6-stream", "1", MvtDecoder.SchemaId, 1, 1, "Handmade synthetic geometry; no OSM", Lumyte.Benchmarks.Maps.DenseMapFixture.Profile)
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => registration.GetKey(id);
    }

    private sealed class ControlledStage(MvtResourceStage inner) : IResourceSourceStage<MapTileContent>, IDisposable
    {
        public SemaphoreSlim BuildPermit { get; } = new(0);

        public SemaphoreSlim ReleasePermit { get; } = new(0);

        public int Builds { get; private set; }

        public int Releases { get; private set; }

        public async ValueTask<MapTileContent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            await BuildPermit.WaitAsync(cancellationToken);
            MapTileContent content = await inner.BuildAsync(context, cancellationToken);
            Builds++;
            return content;
        }

        public void Dispose()
        {
            BuildPermit.Dispose();
            ReleasePermit.Dispose();
        }

        public async ValueTask ReleaseAsync(MapTileContent resource)
        {
            await ReleasePermit.WaitAsync();
            await inner.ReleaseAsync(resource);
            Releases++;
        }
    }

    private sealed class Runtime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }
}
