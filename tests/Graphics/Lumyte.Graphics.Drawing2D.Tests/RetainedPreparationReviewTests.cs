using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Deterministic regressions for disabled active lookup and preparation-only callback reachability.</summary>
public sealed class RetainedPreparationReviewTests
{
    /// <summary>Disabled cold entries never compare active-frame keys.</summary>
    /// <param name="enabled">Selects the unchanged constructor default or explicit opt-in.</param>
    /// <param name="entries">The independently owned cold entry count.</param>
    [Theory]
    [InlineData(null, 1)]
    [InlineData(null, 8)]
    [InlineData(null, 32)]
    [InlineData(false, 32)]
    public void DisabledActiveLookupDoesNotCompareKeys(bool? enabled, int entries)
    {
        using var cache = new RetainedDrawingCache2D(enabled is { } flag ? new() { EnableResidentInstances = flag } : new());
        RetainedDrawingCache2D.FrameLease frame = cache.AcquireFrame();
        RetainedDrawingCache2D.Key key = Key();
        for (int i = 0; i < entries; i++)
        {
            Assert.Null(cache.Take(key, frame));
            frame.Assets.Add(new(key, new Batch(new State())));
        }

        Assert.Equal(0, cache.ActiveKeyComparisons);
        cache.ReleaseFrame(frame, false);
    }

    /// <summary>Enabled same-command cold aliases remain available, but other active frames cannot borrow them.</summary>
    [Fact]
    public void EnabledColdAliasKeepsInitializationProvenance()
    {
        using var cache = new RetainedDrawingCache2D(new() { EnableResidentInstances = true });
        RetainedDrawingCache2D.FrameLease first = cache.AcquireFrame();
        RetainedDrawingCache2D.Key key = Key();
        var producer = new RetainedDrawingCache2D.Asset(key, new ResidentBatchLease2D(new([], true)));
        first.Assets.Add(producer);
        RetainedDrawingCache2D.Asset alias = Assert.IsType<RetainedDrawingCache2D.Asset>(cache.Take(key, first));
        Assert.Same(producer, alias.InitializationSource);
        Assert.False(alias.Resident);
        first.Assets.Add(alias);
        RetainedDrawingCache2D.FrameLease second = cache.AcquireFrame();
        Assert.Null(cache.Take(key, second));
        Assert.True(cache.ActiveKeyComparisons > 0);
        cache.ReleaseFrame(second, false);
        cache.ReleaseFrame(first, false);
    }

    /// <summary>All retained copies lose preparation callbacks before publication or failed cleanup.</summary>
    /// <param name="route">Direct, parent Graph or retained preparation.</param>
    /// <param name="fail">Throws after an asynchronous explicit budget read.</param>
    /// <returns>The asynchronous lifecycle verification.</returns>
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task PublicationAndFailureReleaseEveryCopiedSourceAsync(int route, bool fail)
    {
        var state = new State { Fail = fail };
        var device = new ProfilingDevice();
        var defaults = DrawingCommandRenderers2D.CreateDefault();
        var renderers = new DrawingCommandRenderers2D(new Seed(state), defaults.FillRoundedRect, new Capture(state), defaults.DrawLine, defaults.FillPath, defaults.StrokePath, defaults.DrawImage, defaults.DrawCoverageMask, defaults.DrawDistanceField, defaults.DrawMesh, defaults.DrawPaintGraph);
        using var renderer = new Renderer2D(device, renderers);
        Exception? error = null;
        IDisposable? prepared = null;
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        using var scene = new DrawingScene2D();
        using var graph = new Graph(device);
        using RenderGraphRenderer2D? graphHost = route == 1 ? CreateGraphHost(graph, renderer) : null;
        try
        {
            if (route == 1)
            {
                prepared = await graph.Append2D(graphHost!);
            }
            else if (route == 2)
            {
                scene.Insert(scene.Root, 0, Content());
                using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
                prepared = await renderer.PrepareAsync(snapshot, new() { View = view });
            }
            else
            {
                prepared = await renderer.PrepareAsync(Content(), new() { View = view });
            }
        }
        catch (Exception caught)
        {
            error = caught;
        }

        try
        {
            if (fail)
            {
                Assert.IsType<InvalidOperationException>(error);
            }
            else
            {
                Assert.Null(error);
            }

            Assert.True(state.BeforeAwait);
            Assert.True(state.AfterAwait);
            Assert.True(state.ReadBytes >= 64);
            Assert.NotEmpty(state.Contexts);
            Assert.All(state.Contexts, context => Assert.False(context.HasWorkingBytesSource));
            Assert.True(state.CleanupSawClearedSources);
            Assert.Equal(state.ReadBytes, state.Budget!.OtherWorkingBytes);
            Assert.Equal(0, device.NativeBuffers);
        }
        finally
        {
            prepared?.Dispose();
        }
    }

    /// <summary>Closing a borrowed argument plan cannot invalidate the originating preparation's callback.</summary>
    /// <returns>The asynchronous scope and memoization verification.</returns>
    [Fact]
    public async Task BorrowedPlanClosurePreservesOriginScopeAndResolvedScalarAsync()
    {
        var device = new ProfilingDevice();
        using var resources = new DrawingResourceState2D(device);
        using var origin = new DrawingArgumentPlan2D(device);
        using var borrowed = new DrawingArgumentPlan2D(device);
        using IDisposable scope = origin.HoldWorkingBytesSources();
        var target = new DrawingTargetInfo2D { Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment }, PixelScale = 1 };
        var context = new RenderPreparationContext2D(device, target, resources, origin);
        int calls = 0;
        context.SetWorkingBytesSource(Read);
        RenderPreparationContext2D budget = context.WithArgumentPlan(borrowed).WithWorkingLimit(1000);
        await borrowed.SealAsync();
        Assert.True(context.HasWorkingBytesSource);
        Assert.True(budget.HasWorkingBytesSource);
        Assert.Equal(200, budget.OtherWorkingBytes);
        Assert.Equal(1, calls);
        context.SetWorkingBytesSource(Read);
        RenderPreparationContext2D unread = context.WithArgumentPlan(borrowed);
        Assert.True(unread.HasWorkingBytesSource);
        scope.Dispose();
        Assert.False(context.HasWorkingBytesSource);
        Assert.False(unread.HasWorkingBytesSource);
        Assert.Equal(200, budget.OtherWorkingBytes);
        Assert.Equal(1, calls);

        long Read()
        {
            calls++;
            return 200;
        }
    }

    private static RetainedDrawingCache2D.Key Key() => new([], Matrix3x2.Identity, [], 16, 16, TextureFormat.Rgba16Float, 1);

    private static DrawList2D Content()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        Draw(canvas);
        return canvas.End();
    }

    private static void Draw(DrawingCanvas2D canvas)
    {
        canvas.FillRect(new(2, 2, 4, 4), new SRgba<float>(1, 0, 0, 1));
        canvas.FillCircle(new(8, 8), 2, new SRgba<float>(0, 1, 0, 1));
    }

    private static RenderGraphRenderer2D CreateGraphHost(Graph graph, Renderer2D renderer)
    {
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        Draw(canvas);
        var description = new TextureDesc { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment };
        return new(renderer, canvas.End(), new() { Texture = graph.CreateTexture(description), Description = description }, new(graph));
    }

    private sealed class State
    {
        internal bool Fail { get; init; }

        internal List<RenderPreparationContext2D> Contexts { get; } = [];

        internal RenderPreparationContext2D? Budget { get; set; }

        internal bool BeforeAwait { get; set; }

        internal bool AfterAwait { get; set; }

        internal long ReadBytes { get; set; }

        internal bool CleanupSawClearedSources { get; set; } = true;
    }

    private sealed class Seed(State state) : IFillRectRenderer2D
    {
        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            state.Contexts.Add(context.WithArgumentPlan(context.ArgumentPlan));
            _ = context.ArgumentPlan.Reserve();
            return ValueTask.FromResult<IPreparedCommandBatch2D>(new Batch(state));
        }
    }

    private sealed class Capture(State state) : IFillCircleRenderer2D
    {
        public async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken = default)
        {
            RenderPreparationContext2D fork = context.WithArgumentPlan(context.ArgumentPlan);
            RenderPreparationContext2D budget = fork.WithWorkingLimit(context.MaxWorkingBytes);
            state.Contexts.Add(context);
            state.Contexts.Add(fork);
            state.Contexts.Add(fork.WithArgumentPlan(context.ArgumentPlan));
            state.Budget = budget;
            state.BeforeAwait = fork.HasWorkingBytesSource;
            await Task.Yield();
            state.AfterAwait = fork.HasWorkingBytesSource;
            state.ReadBytes = budget.OtherWorkingBytes;
            if (state.Fail)
            {
                throw new InvalidOperationException("Controlled preparation failure after a valid asynchronous budget read.");
            }

            return new Batch(state);
        }
    }

    private sealed class Batch(State state) : IPreparedCommandBatch2D, IRetainedMemory2D
    {
        public bool HasStableOwners => true;

        public void AccountMemory(Dictionary<object, long> owners) => owners.TryAdd(this, 64);

        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
        }

        public void Dispose() => state.CleanupSawClearedSources &= state.Contexts.All(context => !context.HasWorkingBytesSource);
    }
}
