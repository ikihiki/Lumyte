using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Reproduces reviewed ownership failures using valid original backends that never retain context resources.</summary>
public sealed partial class CompositionMigrationTests
{
    /// <summary>Checks owned service lifetime without requiring any new custom backend protocol.</summary>
    /// <param name="route">The original preparation entry point.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData("direct")]
    [InlineData("retained")]
    [InlineData("graph")]
    public async Task ReviewOwnedManagerOutlivesEveryUnretainedPreparedOwnerAsync(string route)
    {
        var device = new ProfilingDevice();
        var manager = new UnretainedManager();
        var key = new SharedDeclaration<UnretainedManager>("raw", _ => manager);
        IRenderer2D renderer = DefineRaw(key).BuildRenderer(device, Resources(device), null, null);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var target = new RenderTarget2D { View = view };
        if (route == "direct")
        {
            PreparedDraw2D first = await renderer.PrepareAsync(RectCircleRect(), target);
            PreparedDraw2D second = await renderer.PrepareAsync(RectCircleRect(), target);
            renderer.Dispose();
            Assert.Equal(0, manager.Disposals);
            using var commands = new ProfilingCommands();
            first.Record(commands);
            first.Dispose();
            Assert.Equal(0, manager.Disposals);
            second.Record(commands);
            second.Dispose();
        }
        else if (route == "retained")
        {
            using var snapshot = new DrawingSnapshot2D(1, [new(default, RectCircleRect(), Matrix3x2.Identity, [], 1, 1)]);
            PreparedDrawingFrame2D first = await renderer.PrepareAsync(snapshot, target);
            PreparedDrawingFrame2D second = await renderer.PrepareAsync(snapshot, target);
            renderer.Dispose();
            Assert.Equal(0, manager.Disposals);
            Complete(first);
            first.Dispose();
            Assert.Equal(0, manager.Disposals);
            Complete(second);
            second.Dispose();
        }
        else
        {
            using var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(device);
            RenderGraphTexture handle = graph.ImportTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
            var bindings = new DrawingGraphBindings2D(graph);
            bindings.BindTexture(texture, handle);
            var canvas = new RenderGraphCanvas2D();
            canvas.Begin();
            canvas.FillRect(new(0, 0, 8, 8), new SRgba<float>(1, 0, 0, 1));
            var graphTarget = new RenderTargetBinding2D { Texture = handle, Description = new() { Width = texture.Width, Height = texture.Height, Format = texture.Format, Usage = texture.Usage } };
            var host = new RenderGraphRenderer2D(renderer, canvas.End(), graphTarget, bindings);
            renderer.Dispose();
            PreparedDrawingPasses2D prepared = await graph.Append2D(host);
            host.Dispose();
            Assert.Equal(0, manager.Disposals);
            using CompiledRenderGraph compiled = graph.Compile();
            using var commands = new ProfilingCommands();
            compiled.Record(commands);
            commands.Dispose();
            compiled.Dispose();
            prepared.Dispose();
        }

        Assert.True(manager.Records > 0);
        Assert.Equal(1, manager.Disposals);
    }

    /// <summary>Checks custom cleanup retries retain the host-owned manager on each entry point.</summary>
    /// <param name="route">The original preparation entry point.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData("direct")]
    [InlineData("retained")]
    [InlineData("graph")]
    public async Task ReviewUnretainedBatchCleanupFailureKeepsManagerAliveAsync(string route)
    {
        var device = new ProfilingDevice();
        var manager = new UnretainedManager { FailBatchDisposeOnce = true };
        var key = new SharedDeclaration<UnretainedManager>("cleanup", _ => manager);
        IRenderer2D renderer = DefineRaw(key).BuildRenderer(device, Resources(device), null, null);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        if (route == "direct")
        {
            PreparedDraw2D prepared = await renderer.PrepareAsync(RectCircleRect(), new() { View = view });
            renderer.Dispose();
            Assert.Equal(0, manager.Disposals);
            Assert.Throws<AggregateException>(prepared.Dispose);
            Assert.Equal(0, manager.Disposals);
            prepared.Dispose();
            prepared.Dispose();
        }
        else if (route == "retained")
        {
            using var snapshot = new DrawingSnapshot2D(1, [new(default, RectCircleRect(), Matrix3x2.Identity, [], 1, 1)]);
            PreparedDrawingFrame2D frame = await renderer.PrepareAsync(snapshot, new() { View = view });
            renderer.Dispose();
            using var commands = new ProfilingCommands();
            frame.Record(commands);
            commands.Submit();
            using var submission = new CompletedSubmission();
            frame.AttachSubmission(submission);
            commands.Dispose();
            Assert.ThrowsAny<Exception>(frame.Retire);
            Assert.Equal(0, manager.Disposals);
            frame.Retire();
            frame.Dispose();
        }
        else
        {
            using var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(device);
            (RenderGraphRenderer2D host, _) = RawGraphHost(graph, renderer, texture, false);
            renderer.Dispose();
            PreparedDrawingPasses2D prepared = await graph.Append2D(host);
            host.Dispose();
            Assert.Throws<AggregateException>(prepared.Dispose);
            Assert.Equal(0, manager.Disposals);
            prepared.Dispose();
        }

        Assert.Equal(1, manager.Disposals);
    }

    /// <summary>Checks partial Graph preparation cleanup remains retryable after source disposal.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ReviewFailedGraphPreparationReturnsQueuedOwnerThroughHostRetryAsync()
    {
        var device = new ProfilingDevice();
        var manager = new UnretainedManager { FailBatchDisposeOnce = true, FailCirclePreparation = true };
        var key = new SharedDeclaration<UnretainedManager>("partial", _ => manager);
        IRenderer2D renderer = DefineRaw(key).BuildRenderer(device, Resources(device), null, null);
        using var texture = new DispatchTexture();
        using var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(device);
        (RenderGraphRenderer2D host, _) = RawGraphHost(graph, renderer, texture, true);
        renderer.Dispose();
        await Assert.ThrowsAsync<AggregateException>(() => graph.Append2D(host).AsTask());
        Assert.Equal(0, manager.Disposals);
        host.Dispose();
        Assert.Equal(1, manager.Disposals);
        host.Dispose();
        Assert.Equal(1, manager.Disposals);
    }

    /// <summary>Checks external resource ownership aliases are transferred and returned once, including retry.</summary>
    /// <param name="retry">Whether the external owner's first cleanup fails.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReviewSharedAndExternalOwnerAliasDisposesOnce(bool retry)
    {
        var device = new ProfilingDevice();
        var manager = new UnretainedManager { FailDisposeOnce = retry };
        var key = new SharedDeclaration<UnretainedManager>("aliased", _ => manager);
        IRenderer2D renderer = DefineRaw(key).BuildRenderer(device, Resources(device), manager, null);
        if (retry)
        {
            Assert.Throws<InvalidOperationException>(renderer.Dispose);
            Assert.Equal(0, manager.Disposals);
        }

        renderer.Dispose();
        renderer.Dispose();
        Assert.Equal(1, manager.Disposals);
    }

    /// <summary>Checks failed construction keeps caller ownership even when another acquired owner's cleanup needs retry.</summary>
    /// <param name="retry">Whether the independent acquired owner's first cleanup fails.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReviewFailedBuildDoesNotConsumeAliasedCallerOwner(bool retry)
    {
        var device = new ProfilingDevice();
        var caller = new UnretainedManager();
        var acquired = new UnretainedManager { FailDisposeOnce = retry };
        var key = new SharedDeclaration<UnretainedManager>("caller", _ => caller);
        var other = new SharedDeclaration<UnretainedManager>("acquired", _ => acquired);
        var failure = new SharedDeclaration<UnretainedManager>("failure", _ => throw new InvalidOperationException("later factory failed"));
        using RendererBuildHost2D definition = DefineRaw(key, failure, other);
        if (retry)
        {
            Assert.Throws<AggregateException>(() => definition.BuildRenderer(device, Resources(device), caller, null));
            Assert.Equal(0, acquired.Disposals);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => definition.BuildRenderer(device, Resources(device), caller, null));
        }

        Assert.Equal(0, caller.Disposals);
        definition.Dispose();
        Assert.Equal(1, acquired.Disposals);
        Assert.Equal(0, caller.Disposals);
        caller.Dispose();
    }

    /// <summary>Checks logical share keys remain distinct while physical ownership aliases dispose once.</summary>
    [Fact]
    public void ReviewDistinctShareKeysReturningOneInstanceDisposeOnce()
    {
        var device = new ProfilingDevice();
        var manager = new UnretainedManager();
        var one = new SharedDeclaration<UnretainedManager>("one", _ => manager);
        var two = new SharedDeclaration<UnretainedManager>("two", _ => manager);
        RendererBuildHost2D definition = DefineRaw(one, two);
        DrawingResources2D resources = Resources(device);
        Assert.Equal("one", definition.DescribeDataflow(device, resources)[0].SharedIdentity);
        Assert.Equal("two", definition.DescribeDataflow(device, resources)[2].SharedIdentity);
        IRenderer2D renderer = definition.BuildRenderer(device, resources, null, null);
        renderer.Dispose();
        Assert.Equal(1, manager.Disposals);
    }

    private static (RenderGraphRenderer2D Host, RenderTargetBinding2D Target) RawGraphHost(Lumyte.Graphics.RenderGraph.RenderGraph graph, IRenderer2D renderer, DispatchTexture texture, bool circle)
    {
        RenderGraphTexture handle = graph.ImportTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
        var bindings = new DrawingGraphBindings2D(graph);
        bindings.BindTexture(texture, handle);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 8, 8), new SRgba<float>(1, 0, 0, 1));
        if (circle)
        {
            canvas.FillCircle(new(4, 4), 3, new SRgba<float>(0, 1, 0, 1));
        }

        var target = new RenderTargetBinding2D { Texture = handle, Description = new() { Width = texture.Width, Height = texture.Height, Format = texture.Format, Usage = texture.Usage } };
        return (new(renderer, canvas.End(), target, bindings), target);
    }

    private static RendererBuildHost2D DefineRaw(SharedDeclaration<UnretainedManager> key, SharedDeclaration<UnretainedManager>? circle = null, SharedDeclaration<UnretainedManager>? rounded = null) => new(Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: new DrawingRetentionOptions2D())[
        Renderer.FillRect()[key],
        Renderer.FillRoundedRect()[rounded ?? key],
        Renderer.FillCircle()[circle ?? key],
        Renderer.DrawLine()[key],
        Renderer.FillPath()[key],
        Renderer.StrokePath()[key],
        Renderer.DrawImage()[key],
        Renderer.DrawCoverageMask()[key],
        Renderer.DrawDistanceField()[key],
        Renderer.DrawMesh()[key],
        Renderer.DrawPaintGraph()[key]]);

    private sealed class UnretainedManager : PipelineElement2D, IFillRectRenderer2D, IFillRoundedRectRenderer2D, IFillCircleRenderer2D, IDrawLineRenderer2D, IFillPathRenderer2D, IStrokePathRenderer2D, IDrawImageRenderer2D, IDrawCoverageMaskRenderer2D, IDrawDistanceFieldRenderer2D, IDrawMeshRenderer2D, IDrawPaintGraphRenderer2D, IDisposable
    {
        internal int Disposals { get; private set; }

        internal int Records { get; private set; }

        internal bool FailDisposeOnce { get; init; }

        internal bool FailBatchDisposeOnce { get; init; }

        internal bool FailCirclePreparation { get; init; }

        private int DisposeAttempts { get; set; }

        public void Dispose()
        {
            DisposeAttempts++;
            if (FailDisposeOnce && DisposeAttempts == 1)
            {
                throw new InvalidOperationException("owner cleanup retry");
            }

            Disposals++;
        }

        ValueTask<IPreparedCommandBatch2D> IFillRectRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken) => ValueTask.FromResult<IPreparedCommandBatch2D>(new UnretainedBatch(this));

        ValueTask<IPreparedCommandBatch2D> IFillRoundedRectRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken) => ValueTask.FromResult<IPreparedCommandBatch2D>(new UnretainedBatch(this));

        ValueTask<IPreparedCommandBatch2D> IFillCircleRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken)
        {
            if (FailCirclePreparation)
            {
                throw new InvalidOperationException("later command preparation failed");
            }

            return ValueTask.FromResult<IPreparedCommandBatch2D>(new UnretainedBatch(this));
        }

        ValueTask<IPreparedCommandBatch2D> IDrawLineRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands, CancellationToken cancellationToken) => ValueTask.FromResult<IPreparedCommandBatch2D>(new UnretainedBatch(this));

        ValueTask<IPreparedCommandBatch2D> IFillPathRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken) => ValueTask.FromResult<IPreparedCommandBatch2D>(new UnretainedBatch(this));

        ValueTask<IPreparedCommandBatch2D> IStrokePathRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken) => ValueTask.FromResult<IPreparedCommandBatch2D>(new UnretainedBatch(this));

        ValueTask<IPreparedCommandBatch2D> IDrawImageRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken) => ValueTask.FromResult<IPreparedCommandBatch2D>(new UnretainedBatch(this));

        ValueTask<IPreparedCommandBatch2D> IDrawCoverageMaskRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken) => ValueTask.FromResult<IPreparedCommandBatch2D>(new UnretainedBatch(this));

        ValueTask<IPreparedCommandBatch2D> IDrawDistanceFieldRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken) => ValueTask.FromResult<IPreparedCommandBatch2D>(new UnretainedBatch(this));

        ValueTask<IPreparedCommandBatch2D> IDrawMeshRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawMeshCommand2D> commands, CancellationToken cancellationToken) => ValueTask.FromResult<IPreparedCommandBatch2D>(new UnretainedBatch(this));

        ValueTask<IPreparedCommandBatch2D> IDrawPaintGraphRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken) => ValueTask.FromResult<IPreparedCommandBatch2D>(new UnretainedBatch(this));

        internal void CheckAlive() => ObjectDisposedException.ThrowIf(Disposals != 0, this);

        private sealed class UnretainedBatch(UnretainedManager manager) : IPreparedCommandBatch2D
        {
            private int _disposeAttempts;
            private bool _disposed;

            public void DeclareResources(IDrawingResourceDeclarations2D resources) => manager.CheckAlive();

            public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
            {
                manager.CheckAlive();
                manager.Records++;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    manager.CheckAlive();
                    _disposeAttempts++;
                    if (manager.FailBatchDisposeOnce && _disposeAttempts == 1)
                    {
                        throw new InvalidOperationException("batch cleanup retry");
                    }

                    _disposed = true;
                }
            }
        }
    }
}
