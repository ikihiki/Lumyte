using System.Collections.Immutable;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Maps;
using Lumyte.Memory;
using Lumyte.Resources;
using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Deterministic map paint identity guards.</summary>
public sealed class MapPaintIdentityTests
{
    /// <summary>Immutable style paints keep reference identity across features and recordings.</summary>
    /// <returns>The identity contract checks.</returns>
    [Fact]
    public async Task StylePaintIdentitySurvivesRecordingAsync()
    {
        await using var memory = new MemoryManager();
        await using var manager = new ResourceManager("paint-identity", new Source(), [new Stage(false)], memory, new Runtime());
        await using var map = new MapSession(manager, new Tiles(1), Style(false), new(), [new(1, 0, 0), new(1, 1, 0)]);
        map.SetViewport(new(512, 128));
        map.SetCamera(new(0, 66.51326044311186, 1));
        await AdoptAsync(map);
        DrawList2D first = Record(map);
        DrawList2D second = Record(map);
        Assert.Equal(3, first.Count);
        FillRectCommand2D background = Assert.IsType<FillRectCommand2D>(first.Commands[0]);
        FillRectCommand2D nextBackground = Assert.IsType<FillRectCommand2D>(second.Commands[0]);
        Assert.Equal(1, Compile(first).Count(instruction => instruction.Kind == 0 && instruction.A.Z == 1));
        Assert.Same(background.Paint.Root, nextBackground.Paint.Root);

        FillPathCommand2D[] paths = first.Commands.OfType<FillPathCommand2D>().ToArray();
        Assert.Equal(2, paths.Length);
        Assert.Same(paths[0].Paint.Root, paths[1].Paint.Root);
        Assert.Same(paths[0].Paint.Root, Assert.IsType<FillPathCommand2D>(second.Commands[1]).Paint.Root);
    }

    private static IReadOnlyList<DrawingInstruction> Compile(DrawList2D drawing)
    {
        using var plan = new ArgumentTablePlan(new Device());
        var program = new DrawingProgram2D(plan.CreateShaderResources(), new() { Description = new() { Width = 512, Height = 128, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } });
        foreach (DrawingCommand2D command in drawing.Commands)
        {
            program.Compile(command);
        }

        return program.Instructions;
    }

    private static DrawList2D Record(MapSession map)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        map.Record(canvas);
        return canvas.End();
    }

    private static MapStyle Style(bool roads)
    {
        MapStyleLayer[] layers = roads
            ? [new("water", new(0, 0, 1, 1)), new("roads", new(1, 0, 0, 1), new StrokeStyle2D { Width = 2 })]
            : [new("water", new(0, 0, 1, 1))];
        return new("recording", "v1", new(0, 0, 0, 1), layers);
    }

    private static async Task AdoptAsync(MapSession map)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (map.PublishedTiles != 2)
        {
            map.Advance();
            await Task.Delay(1, deadline.Token);
        }
    }

    private sealed class Tiles(int zoom) : MapTileSet("recording", "v1", "recording", zoom, zoom, "handmade", new("recording"))
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => new($"recording/{id.X}/{id.Y}");
    }

    private sealed class Stage(bool dense) : IResourceSourceStage<MapTileContent>
    {
        internal int Builds { get; private set; }

        public ValueTask<MapTileContent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Builds++;
            var polygon = new MapTileFeature(1, MapGeometryType.Polygon, [new([new(-1024, -1024), new(2048, -1024), new(2048, 2048), new(-1024, 2048)], false)], []);
            bool offscreen = dense && context.Address.Id.Value!.EndsWith('3');
            ImmutableArray<MapTileFeature> water = offscreen ? Enumerable.Repeat(polygon, 1000).ToImmutableArray() : [polygon];
            ImmutableArray<MapTileFeature> ineligible = dense ? Enumerable.Repeat(polygon, 1000).ToImmutableArray() : [];
            return ValueTask.FromResult(new MapTileContent([new("water", 1024, water), new("roads", 1024, ineligible)], "v1", 65536, 65536));
        }

        public ValueTask ReleaseAsync(MapTileContent resource) => ValueTask.CompletedTask;
    }

    private sealed class Source : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("v1");
    }

    private sealed class Runtime : IResourceRuntime, IResourceExecutor
    {
        public IResourceExecutor Executor => this;

        public ValueTask BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => context.ExecuteAsync(cancellationToken);

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }

    private sealed class Device : IGraphicDevice
    {
        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
        {
            add
            {
            }

            remove
            {
            }
        }

        public GraphicsDeviceStatus Status => GraphicsDeviceStatus.Ready;

        public GraphicsDeviceLoss? Loss => null;

        public DeviceCaps Caps { get; } = new() { MaxTextureDimension2D = 4096 };

        public IGraphicsQueue Queue => throw new InvalidOperationException("Drawing preparation must not access the queue.");

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Drawing preparation must not wait for device retirement.");

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged => throw new NotSupportedException();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged => throw new InvalidOperationException("Unexpected GPU allocation during external renderer dispatch.");

        public IGraphicsTexture CreateTexture(TextureDesc desc) => throw new InvalidOperationException("Unexpected GPU allocation during external renderer dispatch.");

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw new NotSupportedException();

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw new NotSupportedException();

        public IGraphicsShader CreateShader(ShaderArtifact artifact) => throw new NotSupportedException();

        public IGraphicsSemaphore CreateSemaphore() => throw new NotSupportedException();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => throw new NotSupportedException();

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw new NotSupportedException();

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw new NotSupportedException();

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => throw new NotSupportedException();

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : struct, IShaderArguments => throw new NotSupportedException();
    }
}
