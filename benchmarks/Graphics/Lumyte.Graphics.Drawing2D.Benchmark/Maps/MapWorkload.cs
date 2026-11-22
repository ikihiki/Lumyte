using Lumyte.Graphics.Drawing2D;
using Lumyte.Maps;
using Lumyte.Maps.Mvt;
using Lumyte.Memory;
using Lumyte.Resources;

#if M6_GPU
namespace Lumyte.Benchmarks.GpuMaps;
#else
namespace Lumyte.Benchmarks.Maps;
#endif

/// <summary>Real memory source, decoder, resource pipeline and fixed-session recording.</summary>
public sealed class MapWorkload : IAsyncDisposable
{
    private readonly MemoryManager _memory = new();
    private readonly ResourceManager _resources;

    /// <summary>Initializes a new instance of the <see cref="MapWorkload"/> class with an actual source pipeline and session.</summary>
    public MapWorkload()
    {
        Registration = new("m6-handmade", "1", DenseMapFixture.Profile, new Dictionary<MapTileId, ReadOnlyMemory<byte>> { [DenseMapFixture.Tile] = DenseMapFixture.CreateBytes() });
        Source = new(Registration);
        _resources = new("m6", Source, [new MvtResourceStage(Registration)], _memory, new Runtime());
        Session = new(_resources, new Tiles(Registration), DenseMapFixture.Style(), new(), [DenseMapFixture.Tile]);
        Session.SetViewport(new(512, 512));
        Session.SetCamera(DenseMapFixture.Camera);
    }

    /// <summary>Gets the immutable source registration.</summary>
    public InMemoryMvtRegistration Registration { get; }

    /// <summary>Gets the actual memory tile source.</summary>
    public InMemoryMvtSource Source { get; }

    /// <summary>Gets the owned fixed-session projection and recorder.</summary>
    public MapSession Session { get; }

    /// <summary>Executes the deterministic map workload contract.</summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task WarmAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Session.PublishedTiles != 1)
        {
            Session.Advance();
            if (Session.Failures.Count != 0)
            {
                throw Session.Failures[0];
            }

            await Task.Delay(1, deadline.Token);
        }
    }

    /// <summary>Executes the deterministic map workload contract.</summary>
    /// <returns>The deterministic fixture value.</returns>
    public DrawList2D Record()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        Session.Record(canvas);
        return canvas.End();
    }

    /// <summary>Resets and advances the fixed M3 camera path.</summary>
    /// <param name="frame">Deterministic path sample; no random state.</param>
    public void Move(int frame)
    {
        Session.SetCamera(DenseMapFixture.Camera);
        Session.Pan(new((frame % 16) - 8, (frame % 7) - 3));
        Session.ZoomAt(new(256, 256), (frame % 16) / 32.0);
    }

    /// <summary>Executes the deterministic map workload contract.</summary>
    /// <returns>The deterministic fixture value.</returns>
    public async ValueTask DisposeAsync()
    {
        await Session.DisposeAsync();
        await _resources.DisposeAsync();
        await _memory.DisposeAsync();
    }

    private sealed class Tiles(InMemoryMvtRegistration registration) : MapTileSet("m6", "1", MvtDecoder.SchemaId, 1, 2, "Handmade synthetic geometry; no OSM", DenseMapFixture.Profile)
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => registration.GetKey(id);
    }

    private sealed class Runtime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }
}
