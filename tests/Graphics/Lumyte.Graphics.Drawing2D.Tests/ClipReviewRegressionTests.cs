using System.Collections;
using System.Numerics;
using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises review regressions through public recording and preparation.</summary>
public sealed class ClipReviewRegressionTests
{
    /// <summary>A singular intermediate paint transform stays transparent despite rounded later multiplication.</summary>
    /// <returns>The asynchronous preparation.</returns>
    [Fact]
    public async Task SingularFoldedPaintRemainsTransparentAsync()
    {
        var a = new Matrix3x2(3, 9, 5, 15, 0, 0);
        var b = new Matrix3x2(0.1f, 0.1f, 0.1f, 0.5f, 0, 0);
        Assert.False(Matrix3x2.Invert(a, out _));
        Assert.True(Matrix3x2.Invert(b * a, out _));
        var paint = PaintGraph2D.Clip(Triangle(8), PaintGraph2D.Transform(a, PaintGraph2D.Transform(b, PaintGraph2D.Solid(new(1, 0, 0, 1)))));
        var device = new ProfilingDevice();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 8, 8), paint);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });

        // The public preparation must never upload the opaque red leaf after encountering A.
        Assert.DoesNotContain(device.Instructions, instruction => instruction.Kind == 0 && instruction.A == new Vector4(1, 0, 0, 1));
        Assert.Contains(device.Instructions, instruction => instruction.Kind == 0 && instruction.A == Vector4.Zero);
    }

    /// <summary>Completed and failed preparation cannot leave mask caches pinned by a captured context.</summary>
    /// <param name="retained">Uses the retained entry point.</param>
    /// <param name="failure">Injects a later fault or cancellation.</param>
    /// <returns>The asynchronous preparation.</returns>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public async Task PreparationReleasesMaskCacheAsync(bool retained, int failure)
    {
        var observer = new Observer(failure);
        var defaults = DrawingCommandRenderers2D.CreateDefault();
        var renderers = new DrawingCommandRenderers2D(defaults.FillRect, defaults.FillRoundedRect, defaults.FillCircle, defaults.DrawLine, observer, defaults.StrokePath, defaults.DrawImage, defaults.DrawCoverageMask, defaults.DrawDistanceField, defaults.DrawMesh, defaults.DrawPaintGraph);
        using var renderer = new Renderer2D(new ProfilingDevice { FailWrite = failure == 3 ? 1 : 0 }, renderers, retentionOptions: new() { MaxCachedAssets = 1, MaxCachedBytes = 65536 });
        using var texture = new DispatchTexture { Width = 512, Height = 512 };
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ClipPath(Triangle(512));
        canvas.FillCircle(new(256), 256, new Rgba<float>(1, 1, 1, 1));
        DrawList2D large = canvas.End();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 1, 1), new Rgba<float>(1, 1, 1, 1));
        DrawList2D small = canvas.End();
        canvas.Begin();
        canvas.FillPath(Triangle(1), new Rgba<float>(1, 1, 1, 1));
        DrawList2D probe = canvas.End();
        using var snapshot = new DrawingSnapshot2D(1, [new(default, large, Matrix3x2.Identity, [], 1, 1), new(default, small, Matrix3x2.Identity, [], 1, 1), new(default, probe, Matrix3x2.Identity, [], 1, 1)]);
        async Task PrepareAsync()
        {
            if (retained)
            {
                using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
                AssertReleased(observer.Context!);
                Complete(frame);
            }
            else
            {
                canvas.Begin();
                canvas.Replay(large);
                canvas.Replay(small);
                canvas.Replay(probe);
                using PreparedDraw2D frame = await renderer.PrepareAsync(canvas.End(), new() { View = view });
                AssertReleased(observer.Context!);
            }
        }

        if (failure is 1 or 3)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(PrepareAsync);
        }
        else if (failure == 2)
        {
            await Assert.ThrowsAsync<OperationCanceledException>(PrepareAsync);
        }
        else
        {
            await PrepareAsync();
        }

        Assert.NotNull(observer.Context);
        Assert.True(observer.BytesDuringPreparation >= 1024 * 1024);
        AssertReleased(observer.Context);
        if (retained && failure == 0)
        {
            Assert.Equal(1, renderer.RetentionStatistics.CachedAssets);
            for (int update = 1; update <= 3; update++)
            {
                using var partial = new DrawingSnapshot2D((ulong)(update + 1), [new(default, large, Matrix3x2.CreateTranslation(update, 0), [], (ulong)(update + 1), 1), new(default, small, Matrix3x2.Identity, [], 1, 1), new(default, probe, Matrix3x2.Identity, [], 1, 1)]);
                using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(partial, new() { View = view });
                AssertReleased(observer.Context);
                Complete(frame);
                Assert.Equal(update, renderer.RetentionStatistics.ReusedRuns);
                Assert.Equal(1, renderer.RetentionStatistics.CachedAssets);
                Assert.InRange(renderer.RetentionStatistics.CachedBytes, 1, 65536);
            }
        }
    }

    /// <summary>Graph caches survive deferred uploads, but not abandonment or failed append.</summary>
    /// <param name="mode">Live, abandoned, failed, canceled, culled, or rectangle preparation.</param>
    /// <returns>The asynchronous graph preparation.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task GraphCacheFollowsDeferredPayloadLifetimeAsync(int mode)
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        var description = new TextureDesc { Width = 512, Height = 512, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment };
        RenderGraphTexture target = graph.CreateTexture(description);
        if (mode != 4)
        {
            graph.MarkOutput(target);
        }

        var capture = new CapturingCircle(mode);
        var rectangle = new CapturingRectangle();
        var defaults = DrawingCommandRenderers2D.CreateDefault();
        var table = new DrawingCommandRenderers2D(mode == 5 ? rectangle : defaults.FillRect, defaults.FillRoundedRect, capture, defaults.DrawLine, defaults.FillPath, defaults.StrokePath, defaults.DrawImage, defaults.DrawCoverageMask, defaults.DrawDistanceField, defaults.DrawMesh, defaults.DrawPaintGraph);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.ClipPath(Triangle(512));
        if (mode == 5)
        {
            canvas.FillRect(new(0, 0, 512, 512), new Rgba<float>(1, 1, 1, 1));
            canvas.FillRect(new(1, 1, 510, 510), new Rgba<float>(1, 1, 1, 1));
        }
        else
        {
            canvas.FillCircle(new(256), 256, new Rgba<float>(1, 1, 1, 1));
        }

        using var renderer = new RenderGraphRenderer2D(table, canvas.End(), new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, new DrawingGraphBindings2D(graph));
        if (mode is 2 or 3)
        {
            if (mode == 2)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await graph.Append2D(renderer));
            }
            else
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await graph.Append2D(renderer));
            }

            AssertReleased(capture.Context!);
            return;
        }

        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        RenderPreparationContext2D context = (mode == 5 ? rectangle.Context : capture.Context)!;
        Assert.Equal(1024 * 1024, Bytes(context.Clips));
        if (mode == 1)
        {
            graph.Dispose();
        }
        else
        {
            using CompiledRenderGraph compiled = graph.Compile();
            graph.Dispose();
            using var commands = new ProfilingCommands();
            compiled.Record(commands);
            if (mode is 0 or 5)
            {
                AssertReleased(context);
            }

            if (mode == 0)
            {
                AssertReleased(context);
                Assert.Contains(device.Instructions, instruction => instruction.Kind == 17);
            }

            commands.Dispose();
        }

        AssertReleased(context);
    }

    private static void Complete(PreparedDrawingFrame2D frame)
    {
        var commands = new ProfilingCommands();
        frame.Record(commands);
        commands.Submit();
        frame.AttachSubmission(new Submission());
        commands.Dispose();
        frame.Retire();
    }

    private static void AssertReleased(RenderPreparationContext2D context)
    {
        Assert.Equal(0, Bytes(context.Clips));
        object dictionary = typeof(ClipNormalizationCache2D).GetField("_clips", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context.Clips)!;
        Assert.Empty((IDictionary)dictionary);
        Assert.Equal(0, dictionary.GetType().GetMethod("EnsureCapacity")!.Invoke(dictionary, [0]));
    }

    private static long Bytes(ClipNormalizationCache2D cache) => (long)typeof(ClipNormalizationCache2D)
        .GetField("_bytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;

    private static Path2D Triangle(float size)
    {
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.LineTo(new(size, 0));
        path.LineTo(new(0, size));
        path.Close();
        return path.Build();
    }

    private sealed class Submission : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Completed;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class CapturingRectangle : IFillRectRenderer2D
    {
        internal RenderPreparationContext2D? Context { get; private set; }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken)
        {
            Context = context;
            return ((IFillRectRenderer2D)new DefaultFillRectRenderer2D()).PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class CapturingCircle(int mode) : IFillCircleRenderer2D
    {
        internal RenderPreparationContext2D? Context { get; private set; }

        public async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken)
        {
            Context = context;
            IPreparedCommandBatch2D batch = await ((IFillCircleRenderer2D)new DefaultFillCircleRenderer2D()).PrepareAsync(context, commands, cancellationToken);
            if (mode is 2 or 3)
            {
                batch.Dispose();
                if (mode == 2)
                {
                    throw new InvalidOperationException("Injected append failure.");
                }

                throw new OperationCanceledException(cancellationToken);
            }

            return batch;
        }
    }

    private sealed class Observer(int failure) : IFillPathRenderer2D
    {
        internal RenderPreparationContext2D? Context { get; private set; }

        internal long BytesDuringPreparation { get; private set; }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken)
        {
            Context = context;
            BytesDuringPreparation = Bytes(context.Clips);
            if (failure == 1)
            {
                throw new InvalidOperationException("Injected later preparation failure.");
            }

            if (failure == 2)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            return ValueTask.FromResult<IPreparedCommandBatch2D>(new DispatchBatch(context, "observer", [], false, null));
        }
    }
}
