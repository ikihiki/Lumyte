using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks shared Direct/Graph budgets without requiring an algorithm-specific processor.</summary>
public sealed class SharedWorkingBudgetPropagationTests
{
    /// <summary>Legacy processors and argument-plan forks never force unused cumulative accounting.</summary>
    /// <param name="graphRoute">Selects the parent Graph preparation path.</param>
    /// <param name="groups">The independent custom group count.</param>
    /// <returns>The asynchronous verification.</returns>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 16)]
    [InlineData(false, 32)]
    [InlineData(true, 1)]
    [InlineData(true, 16)]
    [InlineData(true, 32)]
    public async Task UnbudgetedGroupsDoNotCollectPrefixOwnersAsync(bool graphRoute, int groups)
    {
        var state = new State();
        var device = new ProfilingDevice();
        Exception? error = await PrepareAsync(device, state, graphRoute, 1000000, canvas =>
        {
            for (int i = 0; i < groups; i++)
            {
                canvas.Save();
                canvas.ClipRect(new(0, 0, 15 - (i % 4), 15));
                canvas.FillRect(new(2, 2, 4, 4), new SRgba<float>(1, 0, 0, 1));
                canvas.Restore();
            }
        });
        Assert.Null(error);
        Assert.Equal(groups, state.CustomPrepares);
        Assert.Equal(groups, state.CustomDisposals);
        Assert.Equal(0, state.OwnerReads);
        Assert.Equal(0, device.NativeBuffers);
    }

    /// <summary>A processor that opts in after routing still sees the configured cap and previous custom owner.</summary>
    /// <param name="graphRoute">Selects the parent Graph preparation path.</param>
    /// <returns>The asynchronous rejection check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateBudgetOptInReceivesRootCapAndPriorOwnerAsync(bool graphRoute)
    {
        var state = new State { SeedBytes = 990000 };
        var device = new ProfilingDevice();
        Exception? error = await PrepareAsync(device, state, graphRoute, 1000000, canvas =>
        {
            canvas.FillRect(new(2, 2, 4, 4), new SRgba<float>(1, 0, 0, 1));
            canvas.FillCircle(new(8, 8), 2, new SRgba<float>(0, 1, 0, 1));
        });
        Assert.IsType<NotSupportedException>(error);
        Assert.Equal(1000000, state.EffectiveMaximum);
        Assert.True(state.PriorBytes >= state.SeedBytes);
        Assert.Equal(1, state.CustomDisposals);
        Assert.Equal(0, device.NativeBuffers);
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>Retained preparation permits late opt-in without forcing legacy prefix scans.</summary>
    /// <returns>The asynchronous retained admission check.</returns>
    [Fact]
    public async Task RetainedLateOptInReceivesPriorOwnerAsync()
    {
        var state = new State { RetainedRoute = true, SeedBytes = 990000 };
        var device = new ProfilingDevice();
        Exception? error = await PrepareAsync(device, state, false, 1000000, canvas =>
        {
            canvas.FillRect(new(2, 2, 4, 4), new SRgba<float>(1, 0, 0, 1));
            canvas.FillCircle(new(8, 8), 2, new SRgba<float>(0, 1, 0, 1));
        });
        Assert.IsType<NotSupportedException>(error);
        Assert.True(state.PriorBytes >= state.SeedBytes);
        Assert.Equal(1, state.CustomDisposals);
        Assert.Equal(0, device.NativeBuffers);
    }

    /// <summary>Every route admits cumulative owners by budget participation, not by a builtin batch class.</summary>
    /// <param name="route">Direct, parent Graph or retained preparation.</param>
    /// <returns>The asynchronous generic-owner check.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CustomBudgetOwnerKeepsFinalAggregateAdmissionAsync(int route)
    {
        var state = new State { RetainedRoute = route == 2, CustomBudgeted = true, LateBytes = 160000 };
        var device = new ProfilingDevice();
        Exception? error = await PrepareAsync(device, state, route == 1, 150000, canvas =>
        {
            canvas.FillCircle(new(8, 8), 2, new SRgba<float>(0, 1, 0, 1));
            canvas.FillRoundedRect(new(2, 2, 4, 4), 1, new SRgba<float>(1, 0, 0, 1));
        });
        Assert.IsType<NotSupportedException>(error);
        Assert.Equal(150000, state.EffectiveMaximum);
        Assert.Equal(2, state.CustomDisposals);
        Assert.Equal(0, device.NativeBuffers);
    }

    /// <summary>Final aggregate admission includes custom owners returned after budgeted record preparation.</summary>
    /// <param name="graphRoute">Selects the parent Graph preparation path.</param>
    /// <returns>The asynchronous rejection check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinalAggregateIncludesLateCustomOwnerAsync(bool graphRoute)
    {
        var state = new State { LateBytes = 160000 };
        var device = new ProfilingDevice();
        Exception? error = await PrepareAsync(device, state, graphRoute, 150000, canvas =>
        {
            canvas.FillCircle(new(8, 8), 2, new SRgba<float>(0, 1, 0, 1));
            canvas.FillRoundedRect(new(2, 2, 4, 4), 1, new SRgba<float>(1, 0, 0, 1));
        });
        Assert.IsType<NotSupportedException>(error);
        Assert.Equal(150000, state.EffectiveMaximum);
        Assert.Equal(1, state.CustomDisposals);
        Assert.True(device.NativeBuffers > 0);
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>Real legacy record pages retain zero opt-in reservation dictionaries and activation work.</summary>
    /// <param name="graphRoute">Selects the parent Graph preparation path.</param>
    /// <returns>The asynchronous opt-out check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyRecordPagesNeverActivateReferenceBudgetAsync(bool graphRoute)
    {
        var state = new State { LegacyRecords = true };
        var device = new ProfilingDevice();
        Exception? error = await PrepareAsync(device, state, graphRoute, 1000000, canvas =>
        {
            for (int i = 0; i < 16; i++)
            {
                canvas.Save();
                canvas.ClipRect(new(0, 0, 15 - (i % 4), 15));
                canvas.FillRect(new(2, 2, 4, 4), new SRgba<float>(1, 0, 0, 1));
                canvas.Restore();
            }
        });
        Assert.Null(error);
        Assert.NotNull(state.Plan);
        Assert.False(state.Plan.WorkingReferencesEnabled);
        Assert.True(device.NativeBuffers > 0);
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>Legacy record groups on either side of opt-in retain complete pre-materialization generation charges.</summary>
    /// <param name="graphRoute">Includes the opaque parent-Graph argument owner.</param>
    /// <returns>The asynchronous mixed-group check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MixedRecordGroupsReserveEveryGenerationBeforeMaterializationAsync(bool graphRoute)
    {
        var state = new State { LegacyRecords = true };
        var device = new ProfilingDevice();
        const int Repeats = 48;
        Exception? error = await PrepareAsync(device, state, graphRoute, 16000000, canvas =>
        {
            for (int i = 0; i < Repeats; i++)
            {
                canvas.FillRect(new(2, 2, 4, 4), new SRgba<float>(1, 0, 0, 1));
                canvas.FillCircle(new(8, 8), 2, new SRgba<float>(0, 1, 0, 1));
                canvas.FillRoundedRect(new(2, 2, 4, 4), 1, new SRgba<float>(1, 0, 0, 1));
            }
        });
        Assert.Null(error);
        Assert.True(state.CollectingBytes >= 256 + (Repeats * 3L * 4 * 256));
        Assert.True(state.CollectingBytes >= state.CompletedEstimate);
        Assert.True(device.NativeBuffers > 0);
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>Forks retain deferred ownership sources, clamp limits and memoize the requested group value.</summary>
    [Fact]
    public void BudgetForkDefersAndMemoizesPriorOwnerSnapshot()
    {
        var device = new ProfilingDevice();
        using var resources = new DrawingResourceState2D(device);
        using var plan = new DrawingArgumentPlan2D(device);
        var target = new DrawingTargetInfo2D { Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment }, PixelScale = 1 };
        var context = new RenderPreparationContext2D(device, target, resources, plan) { MaxWorkingBytes = 1000 };
        int calls = 0;
        long bytes = 200;
        context.SetWorkingBytesSource(ReadBytes);
        RenderPreparationContext2D fork = context.WithArgumentPlan(plan).WithWorkingLimit(2000);
        Assert.Equal(0, calls);
        Assert.True(fork.EnforceWorkingBudget);
        Assert.Equal(1000, fork.MaxWorkingBytes);
        Assert.Equal(200, fork.OtherWorkingBytes);
        bytes = 300;
        Assert.Equal(200, fork.OtherWorkingBytes);
        Assert.Equal(1, calls);
        context.SetWorkingBytesSource(ReadBytes);
        Assert.Equal(300, context.WithWorkingLimit(500).OtherWorkingBytes);
        Assert.Equal(2, calls);

        long ReadBytes()
        {
            calls++;
            return bytes;
        }
    }

    private static async Task<Exception?> PrepareAsync(ProfilingDevice device, State state, bool graphRoute, long maximum, Action<DrawingCanvas2D> draw)
    {
        var defaults = DrawingCommandRenderers2D.CreateDefault();
        var table = new DrawingCommandRenderers2D(new Seed(state), new Late(state), new BudgetedCircle(state), defaults.DrawLine, defaults.FillPath, defaults.StrokePath, defaults.DrawImage, defaults.DrawCoverageMask, defaults.DrawDistanceField, defaults.DrawMesh, defaults.DrawPaintGraph);
        using var renderer = new Renderer2D(device, table, retentionOptions: new() { MaxWorkingBytes = maximum });
        try
        {
            if (graphRoute)
            {
                var canvas = new RenderGraphCanvas2D();
                canvas.Begin();
                draw(canvas);
                using var graph = new Graph(device);
                var description = new TextureDesc { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment };
                RenderGraphTexture target = graph.CreateTexture(description);
                using var host = new RenderGraphRenderer2D(renderer, canvas.End(), new() { Texture = target, Description = description }, new(graph));
                using PreparedDrawingPasses2D prepared = await graph.Append2D(host);
                state.CompletedEstimate = state.Plan?.EstimatedBytes ?? 0;
            }
            else if (state.RetainedRoute)
            {
                var canvas = new Canvas2D();
                canvas.Begin();
                draw(canvas);
                using var target = new DispatchTexture();
                using IGraphicsTextureView view = target.CreateView();
                using var scene = new DrawingScene2D();
                scene.Insert(scene.Root, 0, canvas.End());
                using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
                using PreparedDrawingFrame2D prepared = await renderer.PrepareAsync(snapshot, new() { View = view });
                state.CompletedEstimate = state.Plan?.EstimatedBytes ?? 0;
            }
            else
            {
                var canvas = new Canvas2D();
                canvas.Begin();
                draw(canvas);
                using var target = new DispatchTexture();
                using IGraphicsTextureView view = target.CreateView();
                using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
                state.CompletedEstimate = state.Plan?.EstimatedBytes ?? 0;
            }

            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private sealed class State
    {
        internal bool RetainedRoute { get; init; }

        internal bool CustomBudgeted { get; init; }

        internal bool LegacyRecords { get; init; }

        internal DrawingArgumentPlan2D? Plan { get; set; }

        internal long CollectingBytes { get; set; }

        internal long CompletedEstimate { get; set; }

        internal long SeedBytes { get; init; } = 64;

        internal long LateBytes { get; init; } = 64;

        internal int CustomPrepares { get; set; }

        internal int CustomDisposals { get; set; }

        internal int OwnerReads { get; set; }

        internal long EffectiveMaximum { get; set; }

        internal long PriorBytes { get; set; }
    }

    private sealed class Seed(State state) : IFillRectRenderer2D
    {
        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            _ = context.WithArgumentPlan(context.ArgumentPlan);
            state.CustomPrepares++;
            state.Plan = context.ArgumentPlan;
            return state.LegacyRecords ? PreparedRecordBatch2D.PrepareAsync(context, commands, cancellationToken)
                : ValueTask.FromResult<IPreparedCommandBatch2D>(new Batch(state, state.SeedBytes));
        }
    }

    private sealed class Late(State state) : IFillRoundedRectRenderer2D
    {
        public async ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            state.CustomPrepares++;
            state.Plan = context.ArgumentPlan;
            if (state.LegacyRecords)
            {
                IPreparedCommandBatch2D batch = await PreparedRecordBatch2D.PrepareAsync(context, commands, cancellationToken);
                state.CollectingBytes = context.ArgumentPlan.EstimatedBytes;
                return batch;
            }

            return new Batch(state, state.LateBytes);
        }
    }

    private sealed class BudgetedCircle(State state) : IFillCircleRenderer2D
    {
        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken = default)
        {
            RenderPreparationContext2D budget = context.WithArgumentPlan(context.ArgumentPlan).WithWorkingLimit(2000000);
            state.EffectiveMaximum = budget.MaxWorkingBytes;
            state.PriorBytes = budget.OtherWorkingBytes;
            return state.CustomBudgeted ? ValueTask.FromResult<IPreparedCommandBatch2D>(new BudgetedBatch(state, budget.EnforceWorkingBudget))
                : PreparedRecordBatch2D.PrepareAsync(budget, commands, cancellationToken);
        }
    }

    private sealed class BudgetedBatch(State state, bool enforced) : IPreparedCommandBatch2D, IRetainedMemory2D, IPreparedWorkingBudget2D
    {
        public bool HasStableOwners => true;

        public bool EnforcesWorkingBudget => enforced;

        public void AccountMemory(Dictionary<object, long> owners) => owners.TryAdd(this, 64);

        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
        }

        public void Dispose() => state.CustomDisposals++;
    }

    private sealed class Batch(State state, long bytes) : IPreparedCommandBatch2D, IRetainedMemory2D
    {
        public bool HasStableOwners => true;

        public void AccountMemory(Dictionary<object, long> owners)
        {
            state.OwnerReads++;
            owners.TryAdd(this, bytes);
        }

        public void DeclareResources(IDrawingResourceDeclarations2D resources)
        {
        }

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target)
        {
        }

        public void Dispose() => state.CustomDisposals++;
    }
}
