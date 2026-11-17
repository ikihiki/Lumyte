using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Tests application-sized retained chunks and explicit parent-graph adoption.</summary>
public sealed class ResidentIntegrationTests
{
    /// <summary>A page replacement rebuilds its four-page chunk, not untouched neighboring chunks.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task DirtyPageRetainsPrefixAndSuffixChunksAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        var target = new RenderTarget2D { View = view };
        DrawList2D original = Content(640);
        using var scene = new DrawingScene2D();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, original);
        scene.SetTransform(node, System.Numerics.Matrix3x2.CreateTranslation(3, 0));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D warm = await renderer.PrepareAsync(snapshot, target);
        using var commands = new ProfilingCommands { CountPayloadBytes = true };
        warm.Record(commands);
        RecordedDrawRange[] before = commands.Events.OfType<RecordedDrawRange>().ToArray();
        Complete(warm, commands);
        Assert.Equal(3, before.Length);
        Assert.Equal(3, renderer.RetentionStatistics.CompiledRuns);

        DrawList2D replacement = Content(64, true);
        scene.ReplaceContent(node, original.WithPage(5, replacement));
        using DrawingSnapshot2D changed = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D old = await renderer.PrepareAsync(snapshot, target);
        using PreparedDrawingFrame2D next = await renderer.PrepareAsync(changed, target);
        using var oldCommands = new ProfilingCommands { CountPayloadBytes = true };
        using var nextCommands = new ProfilingCommands { CountPayloadBytes = true };
        old.Record(oldCommands);
        next.Record(nextCommands);
        RecordedDrawRange[] after = nextCommands.Events.OfType<RecordedDrawRange>().ToArray();
        Assert.Equal(2, renderer.RetentionStatistics.ActiveFrames);
        Complete(next, nextCommands);
        Complete(old, oldCommands);
        Assert.Equal(3, after.Length);
        Assert.Same(before[0].Records, after[0].Records);
        Assert.NotSame(before[1].Records, after[1].Records);
        Assert.Same(before[2].Records, after[2].Records);
        Assert.Equal(4, renderer.RetentionStatistics.CompiledRuns);
        Assert.DoesNotContain("shader-copy", oldCommands.Events);
        Assert.Contains("shader-copy", nextCommands.Events);
        Assert.Equal(0ul, oldCommands.PayloadCopyBytes);
        Assert.InRange(nextCommands.PayloadCopyBytes, 1ul, commands.PayloadCopyBytes - 1);
        Console.WriteLine($"residentChunks initialCommands=640 chunks=3 initialPayloadBytes={commands.PayloadCopyBytes} noOpPayloadBytes={oldCommands.PayloadCopyBytes} dirtyChunkPayloadBytes={nextCommands.PayloadCopyBytes} dirtyPages=1 rebuiltLogicalCommands=256");
    }

    /// <summary>Canonical 64-command pages are not mandatory allocation or draw boundaries.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task InitialCompatiblePagesBatchAcross128CommandsAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, Content(192));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
        using var commands = new ProfilingCommands { CountPayloadBytes = true };
        frame.Record(commands);
        Assert.Single(commands.Events.OfType<RecordedDrawRange>());
        Assert.Equal(1, renderer.RetentionStatistics.CompiledRuns);
        commands.Dispose();
        frame.AbortUnsubmitted();
    }

    /// <summary>Only execution plus actual completion publishes residency; graph disposal does not.</summary>
    /// <param name="used">Whether the graph is actually recorded and submitted.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GraphExecutionOwnsInitializationAndCompletionAsync(bool used)
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, Content(1));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view, LoadOp = AttachmentLoadOp.Clear });
        using var graph = new Graph(device);
        RenderGraphTexture imported = graph.ImportTexture(texture, TextureState.Undefined, default);
        var bindings = new DrawingGraphBindings2D(graph);
        bindings.BindTexture(texture, imported);
        graph.AppendResident2D(frame, new() { Texture = imported, Description = new() { Width = 64, Height = 64, Format = texture.Format, Usage = TextureUsage.RenderAttachment }, PixelScale = 1 }, bindings);
        if (used)
        {
            graph.MarkOutput(imported);
        }

        using CompiledRenderGraph plan = graph.Compile();
        using var commands = new ProfilingCommands { CountPayloadBytes = true };
        if (used)
        {
            plan.Record(commands);
        }

        Assert.Equal(0, renderer.RetentionStatistics.CachedAssets);
        if (used)
        {
            Assert.Contains("shader-copy", commands.Events);
            Assert.Single(commands.Events.OfType<RecordedDrawRange>());
            Assert.Throws<InvalidOperationException>(frame.Dispose);
            Assert.Throws<InvalidOperationException>(plan.Dispose);
            commands.Submit();
            frame.AttachSubmission(new CompletedSubmission());
            commands.Dispose();
            plan.Dispose();
            graph.Dispose();
            frame.Retire();
            Assert.Equal(1, renderer.RetentionStatistics.CachedAssets);
            using PreparedDrawingFrame2D reused = await renderer.PrepareAsync(snapshot, new() { View = view });
            using var reusedCommands = new ProfilingCommands { CountPayloadBytes = true };
            reused.Record(reusedCommands);
            Assert.DoesNotContain("shader-copy", reusedCommands.Events);
            reusedCommands.Dispose();
            reused.AbortUnsubmitted();
        }
        else
        {
            Assert.Empty(commands.Events.OfType<RecordedDrawRange>());
            plan.Dispose();
            graph.Dispose();
            frame.Dispose();
            Assert.Equal(0, renderer.RetentionStatistics.CachedAssets);
        }
    }

    /// <summary>A rejected append leaves no executable callback in the parent graph.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task WrongTargetAppendDoesNotLeaveExecutablePassAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using var wrong = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, Content(1));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
        using var graph = new Graph(device);
        RenderGraphTexture imported = graph.ImportTexture(wrong, TextureState.Undefined, default);
        var bindings = new DrawingGraphBindings2D(graph);
        bindings.BindTexture(wrong, imported);
        var target = new RenderTargetBinding2D { Texture = imported, Description = new() { Width = 64, Height = 64, Format = wrong.Format, Usage = TextureUsage.RenderAttachment }, PixelScale = 1 };
        Assert.Throws<ArgumentException>(() => graph.AppendResident2D(frame, target, bindings));
        using CompiledRenderGraph plan = graph.Compile(capture: new());
        Assert.Empty(Assert.IsType<RenderGraphSnapshot>(plan.Snapshot).Passes);
        using var commands = new ProfilingCommands();
        plan.Record(commands);
        Assert.Empty(commands.Events.OfType<RecordedDrawRange>());
        commands.Dispose();
    }

    /// <summary>Missing sampled bindings are rejected without retaining even a culled graph callback.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task MissingSampledBindingDoesNotPublishPassAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using var sampled = new DispatchTexture { Width = 8, Height = 8 };
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsTextureView imageView = sampled.CreateView();
        using var sampler = new DispatchSampler();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawImage(new Image2D(imageView, sampler), new(0, 0, 8, 8), new(2, 2, 8, 8));
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, canvas.End());
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
        using var graph = new Graph(device);
        RenderGraphTexture imported = graph.ImportTexture(texture, TextureState.Undefined, default);
        var bindings = new DrawingGraphBindings2D(graph);
        bindings.BindTexture(texture, imported);
        var target = new RenderTargetBinding2D { Texture = imported, Description = new() { Width = 64, Height = 64, Format = texture.Format, Usage = TextureUsage.RenderAttachment }, PixelScale = 1 };
        Assert.Throws<InvalidOperationException>(() => graph.AppendResident2D(frame, target, bindings));
        using CompiledRenderGraph plan = graph.Compile(capture: new());
        Assert.Empty(Assert.IsType<RenderGraphSnapshot>(plan.Snapshot).Passes);
    }

    private static DrawList2D Content(int count, bool changed = false)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        if (changed)
        {
            canvas.Translate(new(0.25f, 0));
        }

        for (int i = 0; i < count; i++)
        {
            canvas.FillRect(new(4 + (i % 20), 4 + ((i / 20) % 20), 2, 2), new SRgba<float>(changed ? 0 : 1, changed ? 1 : 0, 0, 0.5f));
        }

        return canvas.End();
    }

    private static void Complete(PreparedDrawingFrame2D frame, ProfilingCommands commands)
    {
        commands.Submit();
        frame.AttachSubmission(new CompletedSubmission());
        commands.Dispose();
        frame.Retire();
    }

    private sealed class CompletedSubmission : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Completed;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
