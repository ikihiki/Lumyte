using System.Collections.Immutable;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Maps.UI;
using Lumyte.Memory;
using Lumyte.Resources;
using Lumyte.UI;
using Xunit;

namespace Lumyte.Maps.Session.Tests;

/// <summary>Deterministic retain, reservation and generation ownership checks.</summary>
public sealed class SessionTests
{
    /// <summary>Moving away never refunds a dispatched retain; invisible pending known tiles remain undispatched.</summary>
    /// <returns>The finite-set terminal ownership checks.</returns>
    [Fact]
    public async Task VisibleDemandChangesKeepRetainReservationUntilTerminalAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage();
        await using var manager = new ResourceManager("visible-drain", new Source(), [stage], memory, new Runtime());
        var tiles = new Tiles();
        await using var map = new MapSession(manager, tiles, Style(), new() { SelectVisibleTiles = true, MaxCompletedTiles = 1 }, [new(1, 0, 0), new(1, 1, 0)]);
        map.SetViewport(new(100, 100));
        map.SetCamera(new(-90, 66.51326044311186, 1));
        map.Advance();
        Assert.Equal(1, map.InFlightTiles);
        long reservation = map.ResidentCpuBytes;
        map.SetCamera(new(90, 66.51326044311186, 1));
        map.Advance();
        Assert.Equal(1, map.InFlightTiles);
        Assert.Equal(reservation, map.ResidentCpuBytes);
        map.SetViewport(default);
        map.Advance();
        Assert.Equal(reservation, map.ResidentCpuBytes);
        map.BeginStop();
        Assert.False(map.IsStopped);
        stage.Complete();
        await map.DisposeAsync();
        Assert.True(map.IsStopped);
        Assert.Equal(0, map.ResidentCpuBytes);
        Assert.Equal(1, stage.Builds);
    }

    /// <summary>The finite stage keeps a successful old demand as bounded adopted data without fetching an invisible pending tile.</summary>
    /// <returns>The invisible terminal adoption checks.</returns>
    [Fact]
    public async Task SuccessfulInvisibleCompletionRemainsChargedUntilDisposalAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage();
        await using var manager = new ResourceManager("invisible-adoption", new Source(), [stage], memory, new Runtime());
        await using var map = new MapSession(manager, new Tiles(), Style(), new() { SelectVisibleTiles = true, MaxCompletedTiles = 1 }, [new(1, 0, 0), new(1, 1, 0)]);
        map.SetViewport(new(100, 100));
        map.SetCamera(new(-90, 66.51326044311186, 1));
        map.Advance();
        map.SetCamera(new(90, 66.51326044311186, 1));
        map.SetViewport(default);
        stage.Complete();
        await AdoptAsync(map);
        Assert.Empty(map.VisibleTiles);
        Assert.Equal(1, map.PublishedTiles);
        Assert.Equal(128, map.ResidentCpuBytes);
        Assert.Equal(0, map.InFlightTiles);
        Assert.Equal(1, stage.Builds);
        await manager.TrimAsync();
        Assert.Equal(0, stage.Releases);
        await map.DisposeAsync();
        await manager.TrimAsync();
        Assert.Equal(1, stage.Releases);
    }

    /// <summary>Cancellation never abandons accepted retains or releases their reservations early.</summary>
    /// <returns>The lifetime checks.</returns>
    [Fact]
    public async Task StopKeepsTerminalOwnershipAndDisposeSharesDrainAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage();
        var runtime = new Runtime();
        await using var manager = new ResourceManager("session-test", new Source(), [stage], memory, runtime);
        var tiles = new Tiles();
        var session = new MapSession(manager, tiles, Style(), new() { MaxInFlightTiles = 4, MaxCompletedTiles = 1 }, [new(1, 0, 0), new(1, 1, 0)]);
        Assert.False(session.IsStopped);
        session.Advance();
        Assert.Equal(0, session.InFlightTiles);
        session.SetViewport(new(100, 100));
        session.Advance();
        Assert.Equal(1, session.InFlightTiles);
        Assert.Equal(tiles.LoadProfile.MaxReservationBytesPerTile, session.ResidentCpuBytes);
        session.BeginStop();
        session.Advance();
        Assert.False(session.IsStopped);
        Task first = session.DisposeAsync().AsTask();
        Task second = session.DisposeAsync().AsTask();
        Assert.Same(first, second);
        Assert.False(first.IsCompleted);
        stage.Complete();
        await first;
        Assert.True(session.IsStopped);
        Assert.Equal(0, session.ResidentCpuBytes);
        Assert.Equal(0, session.PublishedTiles);
        Assert.Equal(1, stage.Builds);
        Assert.Throws<ObjectDisposedException>(() => session.Advance());
        await manager.TrimAsync();
        Assert.Equal(1, stage.Releases);
    }

    /// <summary>Two cache-sharing sessions still pay their full retained size and own independent pins.</summary>
    /// <returns>The sharing checks.</returns>
    [Fact]
    public async Task CacheHitIsFullyChargedAndPinSurvivesOtherSessionAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage();
        stage.Complete();
        await using var manager = new ResourceManager("session-test", new Source(), [stage], memory, new Runtime());
        var tiles = new Tiles();
        await using var first = new MapSession(manager, tiles, Style(), new(), [new(1, 0, 0)]);
        await using var second = new MapSession(manager, tiles, Style(), new(), [new(1, 0, 0)]);
        first.SetViewport(new(100, 100));
        second.SetViewport(new(100, 100));
        await AdoptAsync(first);
        await AdoptAsync(second);
        Assert.Equal(128, first.ResidentCpuBytes);
        Assert.Equal(128, second.ResidentCpuBytes);
        Assert.Equal(1, stage.Builds);
        await first.DisposeAsync();
        await manager.TrimAsync();
        Assert.Equal(0, stage.Releases);
        using var canvas = new Canvas();
        canvas.Begin();
        second.Record(canvas);
        await second.DisposeAsync();
        await manager.TrimAsync();
        Assert.Equal(1, stage.Releases);
    }

    /// <summary>Same-value viewport updates do not invalidate and callbacks cannot reenter.</summary>
    /// <returns>The callback checks.</returns>
    [Fact]
    public async Task DisplayRevisionAndReentrancyAreDeterministicAsync()
    {
        await using var memory = new MemoryManager();
        await using var manager = new ResourceManager("session-test", new Source(), [new Stage()], memory, new Runtime());
        await using var session = new MapSession(manager, new Tiles(), Style(), new(), []);
        session.SetViewport(new(100, 100));
        ulong revision = session.DisplayRevision;
        session.SetViewport(new(100, 100));
        Assert.Equal(revision, session.DisplayRevision);
        session.Changed += () => Assert.Throws<InvalidOperationException>(() => session.Advance());
        session.SetCamera(new(10, 0, 1));
        Assert.Equal(revision + 1, session.DisplayRevision);
    }

    /// <summary>Recording is pure, clipped, and restores borrowed canvas scopes.</summary>
    /// <returns>The CPU recording checks, without a GPU pixel claim.</returns>
    [Fact]
    public async Task RecordingRestoresCanvasAndPreservesGlobalLayerPassesAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage();
        stage.CompleteGeometry();
        await using var manager = new ResourceManager("recording", new Source(), [stage], memory, new Runtime());
        var style = new MapStyle("test", "v1", new(0, 0, 0, 1), [
            new("water", new(0, 0, 1, 0.5f)),
            new("roads", new(0, 0, 0, 1), new StrokeStyle2D { Width = 8 }),
            new("roads", new(1, 1, 1, 1), new StrokeStyle2D { Width = 4 }),
        ]);
        await using var session = new MapSession(manager, new Tiles(), style, new(), [new(1, 0, 0), new(1, 1, 0)]);
        session.SetCamera(new(0, 45, 1));
        session.SetViewport(new(512, 256));
        while (session.PublishedTiles != 2)
        {
            session.Advance();
            await Task.Delay(1);
        }

        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Save();
        canvas.Scale(new(2, 2));
        session.Record(canvas);
        canvas.Restore();
        canvas.FillRect(new(0, 0, 1, 1), new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1));
        DrawList2D drawing = canvas.End();
        Assert.Equal(8, drawing.Count);
        Assert.Equal(2, stage.Builds);
        Assert.Equal(2, session.PublishedTiles);
    }

    /// <summary>Executes real Vulkan drawing and readback for holes and ordered road passes.</summary>
    /// <returns>Actual GPU completion and analytic interior pixel checks.</returns>
    [Trait("Execution", "Gpu")]
    [MapGpuFact]
    public async Task VulkanMapPixelsAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage();
        stage.CompleteGeometry();
        await using var manager = new ResourceManager("gpu-map", new Source(), [stage], memory, new Runtime());
        var style = new MapStyle("test", "v1", new(0, 0, 0, 1), [new("water", new(0, 0, 1, 1)), new("roads", new(1, 0, 0, 1), new StrokeStyle2D { Width = 8 }), new("roads", new(1, 1, 1, 1), new StrokeStyle2D { Width = 4 })]);
        await using var session = new MapSession(manager, new Tiles(), style, new(), [new(1, 0, 0)]);
        session.SetCamera(new(-90, 66.51326044311186, 1));
        session.SetViewport(new(64, 64));
        await AdoptAsync(session);
        var canvas = new Canvas2D();
        canvas.Begin();
        session.Record(canvas);
        DrawList2D list = canvas.End();
        using var device = VulkanDevice.Create();
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 64, Height = 64, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Clear });
        uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb).BytesPerRowAlignment;
        uint pitch = ((256 + alignment - 1) / alignment) * alignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 64, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        var copyRead = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), Before = default, BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, After = color });
        prepared.Record(commands);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copyRead });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 64, Height = 64 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 64 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] pixels = new byte[readback.Count];
        readback.CopyTo(pixels);
        readback.Unmap();

        // Tile-space polygon shell surrounds a narrow central hole; roads cross that hole.
        int center = (int)((32 * pitch) + (32 * 4));
        Assert.Equal(new byte[] { 255, 255, 255, 255 }, pixels[center..(center + 4)]);
        int hole = (int)((20 * pitch) + (32 * 4));
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, pixels[hole..(hole + 4)]);
        int shell = (int)((20 * pitch) + (8 * 4));
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, pixels[shell..(shell + 4)]);
        await File.WriteAllBytesAsync("/tmp/lumyte-map-gpu-rgba.bin", pixels);
        using var surface = new UiSurface(new MapView { Session = session, ClipToBounds = true });
        surface.SetViewport(new(64, 64));
        using UiDrawingScene2D scene = surface.CreateDrawingScene();
        scene.Update();
        Assert.Equal(1, scene.LastRecordingCount);
        scene.Update();
        Assert.Equal(0, scene.LastRecordingCount);
        using DrawingSceneReadLease2D borrow = scene.Borrow();
        using PreparedDrawingFrame2D retained = await renderer.PrepareAsync(borrow, new() { View = view, LoadOp = AttachmentLoadOp.Clear });
        using IGraphicsCommandBuffer retainedCommands = device.CreateCommandBuffer(new());
        retainedCommands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopySource, AfterState = TextureState.ColorAttachment, Before = copyRead, After = color });
        retained.Record(retainedCommands);
        retainedCommands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copyRead });
        retainedCommands.CopyTextureToBuffer(new() { Texture = target, Width = 64, Height = 64 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 64 });
        retainedCommands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        retainedCommands.Finish();
        using IGraphicsSubmission retainedSubmission = device.Queue.Submit([retainedCommands]);
        retained.AttachSubmission(retainedSubmission);
        await retainedSubmission.WaitAsync();
        await readback.MapAsync();
        byte[] retainedPixels = new byte[readback.Count];
        readback.CopyTo(retainedPixels);
        readback.Unmap();
        retainedCommands.Dispose();
        retained.Retire();
        Assert.Equal(pixels, retainedPixels);
    }

    /// <summary>Executor admission failure is retryable and never consumes a permanent fixed-set slot.</summary>
    /// <returns>The queue-full retry checks.</returns>
    [Fact]
    public async Task QueueFullReturnsToPendingAndRetriesAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage();
        stage.Complete();
        var runtime = new Runtime { RejectOnce = true };
        await using var manager = new ResourceManager("queue-full", new Source(), [stage], memory, runtime);
        await using var session = new MapSession(manager, new Tiles(), Style(), new(), [new(1, 0, 0)]);
        session.SetViewport(new(100, 100));
        await AdoptAsync(session);
        Assert.Empty(session.Failures);
        Assert.Equal(1, stage.Builds);
    }

    /// <summary>A fixed-set larger than the byte budget remains bounded instead of overcommitting.</summary>
    /// <returns>The resident capacity checks.</returns>
    [Fact]
    public async Task FixedSetDoesNotOvercommitOrDispatchAtZeroViewportAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage();
        stage.Complete();
        var tiles = new Tiles();
        await using var manager = new ResourceManager("capacity", new Source(), [stage], memory, new Runtime());
        await using var session = new MapSession(manager, tiles, Style(), new() { MaxResidentCpuBytes = tiles.LoadProfile.MaxReservationBytesPerTile }, [new(1, 0, 0), new(1, 1, 0)]);
        session.SetViewport(new(100, 100));
        session.Advance();
        session.SetViewport(default);
        for (int i = 0; i < 5; i++)
        {
            session.Advance();
            await Task.Delay(1);
        }

        Assert.Equal(1, session.PublishedTiles);
        Assert.Equal(1, stage.Builds);
        Assert.True(session.ResidentCpuBytes <= tiles.LoadProfile.MaxReservationBytesPerTile);
    }

    /// <summary>Terminal decoder failures release the reservation, remain diagnostic and never auto-repeat.</summary>
    /// <returns>The terminal failure checks.</returns>
    [Fact]
    public async Task FailedBuildDoesNotPublishOrAutomaticallyRetryAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage();
        stage.Fail();
        await using var manager = new ResourceManager("failure", new Source(), [stage], memory, new Runtime());
        await using var map = new MapSession(manager, new Tiles(), Style(), new(), [new(1, 0, 0)]);
        map.SetViewport(new(100, 100));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (map.Failures.Count == 0)
        {
            map.Advance();
            await Task.Delay(1, deadline.Token);
        }

        for (int index = 0; index < 5; index++)
        {
            map.Advance();
        }

        Assert.IsType<InvalidDataException>(Assert.Single(map.Failures));
        Assert.Equal(1, stage.Builds);
        Assert.Equal(0, map.ResidentCpuBytes);
        Assert.Equal(0, map.PublishedTiles);
        map.BeginStop();
        map.BeginStop();
        Assert.True(map.IsStopped);
        Assert.Throws<InvalidOperationException>(() => map.SetCamera(new(0, 0, 1)));
        Assert.Throws<InvalidOperationException>(() => map.SetViewport(default));
    }

    /// <summary>A completed but uncollected retain is drained after stop without publication or notification.</summary>
    /// <returns>The completed-slot stop checks.</returns>
    [Fact]
    public async Task StopDiscardsUncollectedSuccessAndCallbackFailureIsNotRepeatedAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage();
        stage.Complete();
        await using var manager = new ResourceManager("stop-complete", new Source(), [stage], memory, new Runtime());
        await using var map = new MapSession(manager, new Tiles(), Style(), new(), [new(1, 0, 0)]);
        map.SetViewport(new(100, 100));
        int notifications = 0;
        map.Changed += () => notifications++;
        map.Advance();
        map.BeginStop();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!map.IsStopped)
        {
            map.Advance();
            await Task.Delay(1, deadline.Token);
        }

        Assert.Equal(0, notifications);
        Assert.Equal(0, map.PublishedTiles);
        Assert.Equal(0, map.ResidentCpuBytes);
        await using var other = new MapSession(manager, new Tiles(), Style(), new(), []);
        other.Changed += () => throw new InvalidOperationException("callback failure");
        Assert.Throws<InvalidOperationException>(() => other.SetViewport(new(40, 40)));
        ulong revision = other.DisplayRevision;
        other.Advance();
        Assert.Equal(revision, other.DisplayRevision);
    }

    /// <summary>Invalid fixed demand is rejected before dispatching any resources.</summary>
    /// <returns>The manifest range check.</returns>
    [Fact]
    public async Task FixedDemandMustRespectManifestZoomRangeAsync()
    {
        await using var memory = new MemoryManager();
        var stage = new Stage();
        await using var manager = new ResourceManager("manifest", new Source(), [stage], memory, new Runtime());
        Assert.Throws<ArgumentOutOfRangeException>(() => new MapSession(manager, new Tiles(), Style(), new(), [new(0, 0, 0)]));
        Assert.Equal(0, stage.Builds);
    }

    private static MapStyle Style() => new("test", "v1", new(0, 0, 0, 1), []);

    private static async Task AdoptAsync(MapSession session)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (session.PublishedTiles == 0)
        {
            session.Advance();
            await Task.Delay(1, deadline.Token);
        }
    }

    private sealed class Tiles() : MapTileSet("tests", "v1", "test", 1, 1, "handmade", new("test"))
    {
        public override ResourceKey<MapTileContent> GetKey(MapTileId id) => new($"test/{id.Zoom}/{id.X}/{id.Y}");
    }

    private sealed class Source : IResourceSource
    {
        public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>("v1");
    }

    private sealed class Stage : IResourceSourceStage<MapTileContent>
    {
        private readonly TaskCompletionSource<MapTileContent> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Builds { get; private set; }

        internal int Releases { get; private set; }

        public async ValueTask<MapTileContent> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
        {
            Builds++;
            return await _completion.Task;
        }

        public ValueTask ReleaseAsync(MapTileContent resource)
        {
            Releases++;
            return ValueTask.CompletedTask;
        }

        internal void CompleteGeometry()
        {
            var polygon = new MapTileFeature(1, MapGeometryType.Polygon, [new([new(0, 0), new(1024, 0), new(1024, 1024), new(0, 1024)], false), new([new(480, 480), new(480, 544), new(544, 544), new(544, 480)], true)], []);
            var road = new MapTileFeature(2, MapGeometryType.LineString, [new([new(-100, 512), new(1124, 512)], false)], []);
            _completion.TrySetResult(new([new("water", 1024, [polygon]), new("roads", 1024, [road])], "v1", 1024, 2048));
        }

        internal void Fail() => _completion.TrySetException(new InvalidDataException("malformed tile"));

        internal void Complete() => _completion.TrySetResult(new(ImmutableArray<MapTileLayer>.Empty, "v1", 128, 256));
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
                return ValueTask.FromException(new Lumyte.Execution.ExecutionQueueFullException("test admission full"));
            }

            return context.ExecuteAsync(cancellationToken);
        }

        public ValueTask ReleaseAsync(IResourceStage stage, IResourceRuntime runtime, object resource) => stage.ReleaseAsync(resource, runtime);
    }

    private sealed class Canvas : DrawingCanvas2D, IDisposable
    {
        public void Dispose()
        {
        }
    }
}
