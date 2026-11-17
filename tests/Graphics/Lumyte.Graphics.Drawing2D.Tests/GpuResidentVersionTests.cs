using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks actual prepared buffer references, uploads and overlapping immutable versions.</summary>
public sealed class GpuResidentVersionTests
{
    /// <summary>Unchanged and integer-moved nodes share native payloads across three live versions.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ThreeLiveVersionsRetainUnchangedGpuIdentityAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        var target = new RenderTarget2D { View = view };
        using var scene = new DrawingScene2D();
        DrawingNodeId2D first = scene.Insert(scene.Root, 0, Content(0));
        scene.Insert(scene.Root, 1, Content(1));
        using DrawingSnapshot2D original = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D warm = await renderer.PrepareAsync(original, target);
        using var warmCommands = new ProfilingCommands();
        warm.Record(warmCommands);
        RecordedDrawRange[] initialized = warmCommands.Events.OfType<RecordedDrawRange>().ToArray();
        Assert.Equal(2, initialized.Length);
        Complete(warm, warmCommands);
        int allocations = device.NativeBuffers;
        int shaders = device.ShaderCreations;
        int pipelines = device.PipelineCreations;
        Assert.True(shaders > 0);
        Assert.True(pipelines > 0);
        long compiled = renderer.RetentionStatistics.CompiledRuns;

        using PreparedDrawingFrame2D old = await renderer.PrepareAsync(original, target);
        scene.SetTransform(first, Matrix3x2.CreateTranslation(3, -2));
        using DrawingSnapshot2D movedSnapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D moved = await renderer.PrepareAsync(movedSnapshot, target);
        Assert.Equal(allocations, device.NativeBuffers);
        Assert.Equal(shaders, device.ShaderCreations);
        Assert.Equal(pipelines, device.PipelineCreations);
        Assert.Equal(compiled, renderer.RetentionStatistics.CompiledRuns);
        scene.ReplaceContent(first, Content(2));
        using DrawingSnapshot2D replacedSnapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D replaced = await renderer.PrepareAsync(replacedSnapshot, target);
        Assert.Equal(compiled + 1, renderer.RetentionStatistics.CompiledRuns);
        Assert.Equal(3, renderer.RetentionStatistics.ActiveFrames);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await renderer.PrepareAsync(original, target));
        scene.Dispose();

        using var oldCommands = new ProfilingCommands();
        using var movedCommands = new ProfilingCommands();
        using var replacedCommands = new ProfilingCommands();
        old.Record(oldCommands);
        moved.Record(movedCommands);
        replaced.Record(replacedCommands);
        RecordedDrawRange[] before = oldCommands.Events.OfType<RecordedDrawRange>().ToArray();
        RecordedDrawRange[] after = movedCommands.Events.OfType<RecordedDrawRange>().ToArray();
        RecordedDrawRange[] changed = replacedCommands.Events.OfType<RecordedDrawRange>().ToArray();
        for (int i = 0; i < 2; i++)
        {
            Assert.Same(initialized[i].Records, before[i].Records);
            Assert.Same(before[i].Records, after[i].Records);
            Assert.Same(before[i].PageMetadata, after[i].PageMetadata);
        }

        Assert.Same(before[1].Records, changed[1].Records);
        Assert.NotSame(before[0].Records, changed[0].Records);
        Assert.Equal(3f, BitConverter.UInt32BitsToSingle(after[0].Reserved0));
        Assert.Equal(-2f, BitConverter.UInt32BitsToSingle(after[0].Reserved1));
        Assert.Equal(0u, before[0].Reserved0);
        Assert.DoesNotContain("copy", oldCommands.Events);
        Assert.DoesNotContain("shader-copy", oldCommands.Events);
        Assert.DoesNotContain("copy", movedCommands.Events);
        Assert.DoesNotContain("shader-copy", movedCommands.Events);
        Assert.Contains("shader-copy", replacedCommands.Events);
        Complete(moved, movedCommands);
        Complete(replaced, replacedCommands);
        Complete(old, oldCommands);
        Assert.Throws<ObjectDisposedException>(old.Retire);
        Assert.Throws<ObjectDisposedException>(moved.Retire);
        Assert.Throws<ObjectDisposedException>(replaced.Retire);
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
        renderer.Dispose();
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>Resolved placement, not a possibly unchanged host revision, selects the property binding.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task FrozenHostEntriesWithZeroRevisionStillUpdateGpuPropertiesAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        DrawList2D content = Content(0);
        var target = new RenderTarget2D { View = view };
        using var original = new DrawingSnapshot2D(0, [new(default, content, Matrix3x2.Identity, [], 1, 1)]);
        using PreparedDrawingFrame2D warm = await renderer.PrepareAsync(original, target);
        using var commands = new ProfilingCommands();
        warm.Record(commands);
        Complete(warm, commands);
        using var moved = new DrawingSnapshot2D(0, [new(default, content, Matrix3x2.CreateTranslation(7, 4), [], 1, 1)]);
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(moved, target);
        using var next = new ProfilingCommands();
        frame.Record(next);
        RecordedDrawRange range = Assert.Single(next.Events.OfType<RecordedDrawRange>());
        Assert.Equal(7f, BitConverter.UInt32BitsToSingle(range.Reserved0));
        Assert.Equal(4f, BitConverter.UInt32BitsToSingle(range.Reserved1));
        Assert.Equal(1, renderer.RetentionStatistics.CompiledRuns);
        next.Dispose();
        frame.AbortUnsubmitted();
    }

    /// <summary>One initialization producer serves two placements, without publishing unfinished residency.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task ColdSharedContentHasOneProducerButUnfinishedFramesNeverShareAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        var target = new RenderTarget2D { View = view };
        using var scene = new DrawingScene2D();
        DrawList2D content = Content(0);
        scene.Insert(scene.Root, 0, content);
        DrawingNodeId2D second = scene.Insert(scene.Root, 1, content);
        scene.SetTransform(second, Matrix3x2.CreateTranslation(3, 4));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D first = await renderer.PrepareAsync(snapshot, target);
        Assert.Equal(1, renderer.RetentionStatistics.CompiledRuns);
        using PreparedDrawingFrame2D pending = await renderer.PrepareAsync(snapshot, target);
        Assert.Equal(2, renderer.RetentionStatistics.CompiledRuns);
        using var commands = new ProfilingCommands();
        first.Record(commands);
        RecordedDrawRange[] roots = commands.Events.OfType<RecordedDrawRange>().ToArray();
        Assert.Equal(2, roots.Length);
        Assert.Same(roots[0].Records, roots[1].Records);
        Assert.Equal(1, commands.Events.Count(value => Equals(value, "shader-copy")));
        Assert.Equal(1, commands.Events.Count(value => Equals(value, "copy")));
        commands.Dispose();
        first.AbortUnsubmitted();
        pending.Dispose();
        Assert.Equal(0, renderer.RetentionStatistics.CachedAssets);
        using PreparedDrawingFrame2D retry = await renderer.PrepareAsync(snapshot, target);
        Assert.Equal(3, renderer.RetentionStatistics.CompiledRuns);
    }

    /// <summary>Dependency-changing properties do not enter the exact translation subset.</summary>
    [Fact]
    public void TranslationEligibilityRejectsDependentGeometryAndClipChanges()
    {
        DrawList2D content = Content(0);
        var entry = new DrawingSceneEntry2D(default, content, Matrix3x2.CreateTranslation(1, 2), [], 1, 1);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 64, Height = 64, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment }, PixelScale = 1 };
        Assert.True(Renderer2D.CanTranslateResident(entry, target));
        Assert.False(Renderer2D.CanTranslateResident(entry with { Transform = Matrix3x2.CreateTranslation(0.5f, 0) }, target));
        Assert.False(Renderer2D.CanTranslateResident(entry with { Transform = Matrix3x2.CreateScale(2) }, target));
        Assert.False(Renderer2D.CanTranslateResident(entry, target with { PixelScale = 2 }));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipRect(new(3, 3, 8, 8));
        canvas.FillRect(new(4, 4, 8, 9), new SRgba<float>(1, 0, 0, 1));
        Assert.False(Renderer2D.CanTranslateResident(entry with { Content = canvas.End() }, target));
    }

    /// <summary>A failed completion keeps the last native lease alive across sibling and renderer disposal.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task FailedCompletionRetainsSharedOwnerUntilRetryAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        var target = new RenderTarget2D { View = view };
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, Content(0));
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        using PreparedDrawingFrame2D warm = await renderer.PrepareAsync(snapshot, target);
        using var initialize = new ProfilingCommands();
        warm.Record(initialize);
        Complete(warm, initialize);
        using PreparedDrawingFrame2D pending = await renderer.PrepareAsync(snapshot, target);
        using PreparedDrawingFrame2D sibling = await renderer.PrepareAsync(snapshot, target);
        using var first = new ProfilingCommands();
        using var second = new ProfilingCommands();
        pending.Record(first);
        sibling.Record(second);
        IShaderReference reference = Assert.IsAssignableFrom<IShaderReference>(Assert.Single(first.Events.OfType<RecordedDrawRange>()).Records);
        var submission = new CompletedSubmission { Status = SubmissionStatus.Failed };
        first.Submit();
        pending.AttachSubmission(submission);
        first.Dispose();
        Assert.Throws<InvalidOperationException>(pending.Retire);
        Complete(sibling, second);
        renderer.Dispose();
        Assert.DoesNotContain(reference.Resource, device.DisposedBuffers);
        Assert.Equal(1, renderer.RetentionStatistics.ActiveFrames);
        submission.Status = SubmissionStatus.Completed;
        pending.Retire();
        pending.Dispose();
        pending.Dispose();
        Assert.Contains(reference.Resource, device.DisposedBuffers);
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>Native owners are charged once while each added lease is charged separately.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task SharedNativeOwnerChargesOnlyOnePayloadAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        var target = new RenderTarget2D { View = view };
        using var scene = new DrawingScene2D();
        DrawList2D content = Content(0);
        scene.Insert(scene.Root, 0, content);
        using (DrawingSnapshot2D snapshot = scene.CaptureSnapshot())
        using (PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, target))
        using (var commands = new ProfilingCommands())
        {
            frame.Record(commands);
            Complete(frame, commands);
        }

        long bytes = renderer.RetentionStatistics.CachedBytes;
        int allocations = device.NativeBuffers;
        scene.Insert(scene.Root, 1, content);
        using (DrawingSnapshot2D snapshot = scene.CaptureSnapshot())
        using (PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, target))
        using (var commands = new ProfilingCommands())
        {
            frame.Record(commands);
            Complete(frame, commands);
        }

        Assert.Equal(allocations, device.NativeBuffers);
        Assert.Equal(bytes + 264, renderer.RetentionStatistics.CachedBytes);
        Assert.Equal(2, renderer.RetentionStatistics.CachedAssets);
    }

    /// <summary>Capture admission rejects before clip arrays, transformed commands or native payloads are materialized.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CaptureBudgetRejectsBeforeNativeAllocationAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true, MaxWorkingBytes = 1024 * 1024 });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        using var snapshot = new DrawingSnapshot2D(0, [new(default, Content(0), Matrix3x2.Identity, new UnmaterializableClips(), 0, 0)]);
        await Assert.ThrowsAsync<NotSupportedException>(async () => await renderer.PrepareAsync(snapshot, new() { View = view }));
        Assert.Equal(0, device.NativeBuffers);
        Assert.Empty(device.Tables);
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
    }

    /// <summary>Warmed payloads still count toward the aggregate prepared-frame working set.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CacheHitsCannotBypassWorkingBudgetAsync()
    {
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { EnableResidentInstances = true, MaxWorkingBytes = 3000000 });
        using var texture = new DispatchTexture { Width = 64, Height = 64 };
        using IGraphicsTextureView view = texture.CreateView();
        DrawList2D first = Many(0);
        DrawList2D second = Many(1);
        foreach (DrawList2D content in new[] { first, second })
        {
            using var snapshot = new DrawingSnapshot2D(0, [new(default, content, Matrix3x2.Identity, [], 0, 0)]);
            using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
            using var commands = new ProfilingCommands();
            frame.Record(commands);
            Complete(frame, commands);
        }

        int allocations = device.NativeBuffers;
        long compiled = renderer.RetentionStatistics.CompiledRuns;
        using var pair = new DrawingSnapshot2D(0, Enumerable.Range(0, 8).Select(index => new DrawingSceneEntry2D(default, index % 2 == 0 ? first : second, Matrix3x2.Identity, [], 0, 0)).ToArray());
        await Assert.ThrowsAsync<NotSupportedException>(async () => await renderer.PrepareAsync(pair, new() { View = view }));
        Assert.Equal(allocations, device.NativeBuffers);
        Assert.Equal(compiled, renderer.RetentionStatistics.CompiledRuns);
        Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);

        static DrawList2D Many(int variant)
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            for (int i = 0; i < 200; i++)
            {
                canvas.FillRect(new(4 + (i % 20), 4 + (i / 20), 2, 2), new SRgba<float>(variant, 1 - variant, 0, 0.5f));
            }

            return canvas.End();
        }
    }

    private static DrawList2D Content(int variant)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(4 + variant, 4, 8, 9), new SRgba<float>(1, 0, variant / 2f, 0.5f));
        return canvas.End();
    }

    private static void Complete(PreparedDrawingFrame2D frame, ProfilingCommands commands)
    {
        commands.Submit();
        frame.AttachSubmission(new CompletedSubmission());
        commands.Dispose();
        frame.Retire();
    }

    private sealed class UnmaterializableClips : IReadOnlyList<DrawingSceneClip2D>
    {
        public int Count => 100000;

        public DrawingSceneClip2D this[int index] => throw new ArithmeticException("Clip materialized before budget admission.");

        public IEnumerator<DrawingSceneClip2D> GetEnumerator() => throw new ArithmeticException("Clip materialized before budget admission.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CompletedSubmission : IGraphicsSubmission
    {
        public SubmissionStatus Status { get; set; } = SubmissionStatus.Completed;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
