using System.Reflection;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Pipelines;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Lumyte.Pipelines;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises the real generic build and drawing graph binding without shaders or submission.</summary>
public sealed class GenericGraphBindingTests
{
    /// <summary>Generic direct and retained owners preserve live-image admission after preparation.</summary>
    /// <param name="retained">Whether the immutable source is a retained snapshot.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericPreparedOwnerPinsLiveImage(bool retained)
    {
        using var fixture = new Fixture();
        using var texture = new OriginTexture(fixture.Device);
        using IGraphicsTextureView view = texture.CreateView();
        Image2D image = new(view, new DispatchSampler());
        ImageResourceRegistry2D.Resource resource = ImageResourceRegistry2D.Register(image);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawImage(image, new(0, 0, 1, 1), new(0, 0, 1, 1));
        DrawList2D list = canvas.End();
        var target = new RenderTarget2D { View = view };
        if (retained)
        {
            using var snapshot = new DrawingSnapshot2D(1, [new(default, list, System.Numerics.Matrix3x2.Identity, [], 1, 1)]);
            using PreparedDrawingFrame2D prepared = fixture.Complete(fixture.Renderer.PrepareAsync(snapshot, target).AsTask());
            snapshot.Dispose();
            Assert.True(resource.IsReferenced);
            Assert.Null(resource.TryAcquireWrite());
            using var commands = new OriginCommands(fixture.Device);
            prepared.Record(commands);
            commands.Dispose();
            prepared.AbortUnsubmitted();
        }
        else
        {
            using PreparedDraw2D prepared = fixture.Complete(fixture.Renderer.PrepareAsync(list, target).AsTask());
            Assert.True(resource.IsReferenced);
            Assert.Null(resource.TryAcquireWrite());
        }

        Assert.False(resource.IsReferenced);
        using ImageResourceRegistry2D.Resource.WriteLease writer = resource.TryAcquireWrite()!;
        Assert.NotNull(writer);
    }

    /// <summary>Generic preparation rejects an active live-image writer before invoking the pipeline.</summary>
    [Fact]
    public void GenericPreparationRejectsLiveImageWriter()
    {
        using var fixture = new Fixture();
        using var texture = new OriginTexture(fixture.Device);
        using IGraphicsTextureView view = texture.CreateView();
        Image2D image = new(view, new DispatchSampler());
        ImageResourceRegistry2D.Resource resource = ImageResourceRegistry2D.Register(image);
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.DrawImage(image, new(0, 0, 1, 1), new(0, 0, 1, 1));
        using ImageResourceRegistry2D.Resource.WriteLease writer = resource.TryAcquireWrite()!;
        Assert.Throws<InvalidOperationException>(() => fixture.Complete(fixture.Renderer.PrepareAsync(canvas.End(), new() { View = view }).AsTask()));
        Assert.Empty(fixture.Requests);
        Assert.False(resource.IsReferenced);
    }

    /// <summary>A mixed contiguous sequence is prepared once with its actual graph target.</summary>
    [Fact]
    public void MixedCommandsUseOnePipelinePreparationAndRecordThePreparedTarget()
    {
        using var fixture = new Fixture();
        using var graph = new Graph(fixture.Device);
        using var texture = new OriginTexture(fixture.Device);
        RenderTargetBinding2D target = Target(graph, texture);
        using var renderer = new RenderGraphRenderer2D(fixture.Renderer, List(), target, new(graph));
        using PreparedDrawingPasses2D prepared = fixture.Complete(graph.Append2D(renderer).AsTask());
        Assert.Single(fixture.Requests);
        Assert.Collection(fixture.Requests[0].Commands, c => Assert.IsType<FillRectCommand2D>(c), c => Assert.IsType<FillCircleCommand2D>(c), c => Assert.IsType<FillRectCommand2D>(c));
        Assert.Same(texture, fixture.PreparedTarget!.Texture);
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Equal(1, compiled.PassNames.Count(name => name == "drawing2d.batch"));
        using var commands = new OriginCommands(fixture.Device);
        compiled.Record(commands);
        Assert.Equal(1, fixture.Records);
        commands.Dispose();
        compiled.Dispose();
    }

    /// <summary>A closed pipeline is rejected on each Append before even the begin pass is added.</summary>
    [Fact]
    public void DisposedPipelineCannotMutateGraph()
    {
        using var fixture = new Fixture();
        using var graph = new Graph(fixture.Device);
        using var texture = new OriginTexture(fixture.Device);
        using var renderer = new RenderGraphRenderer2D(fixture.Renderer, List(), Target(graph, texture), new(graph));
        fixture.Pipeline.Dispose();
        Assert.Throws<ObjectDisposedException>(() => fixture.Complete(graph.Append2D(renderer).AsTask()));
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Empty(compiled.PassNames);
        Assert.Empty(fixture.Requests);
    }

    /// <summary>The existing source coordinator validates before graph mutation, without loading legacy resources.</summary>
    [Fact]
    public void InvalidSourceGenerationCannotMutateGraph()
    {
        bool valid = true;
        using var fixture = new Fixture(() =>
        {
            if (!valid)
            {
                throw new InvalidOperationException("Source generation expired.");
            }
        });
        using var graph = new Graph(fixture.Device);
        using var texture = new OriginTexture(fixture.Device);
        using var renderer = new RenderGraphRenderer2D(fixture.Renderer, List(), Target(graph, texture), new(graph));
        valid = false;
        Assert.Throws<InvalidOperationException>(() => fixture.Complete(graph.Append2D(renderer).AsTask()));
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Empty(compiled.PassNames);
        Assert.Empty(fixture.Requests);
        valid = true;
    }

    /// <summary>Source revision changes after preparation still reject recording before any pipeline callback.</summary>
    [Fact]
    public void SourceGenerationIsRevalidatedAfterGraphPreparation()
    {
        bool valid = true;
        using var fixture = new Fixture(() =>
        {
            if (!valid)
            {
                throw new InvalidOperationException("Source generation expired.");
            }
        });
        using var graph = new Graph(fixture.Device);
        using var texture = new OriginTexture(fixture.Device);
        using var renderer = new RenderGraphRenderer2D(fixture.Renderer, List(), Target(graph, texture), new(graph));
        using PreparedDrawingPasses2D prepared = fixture.Complete(graph.Append2D(renderer).AsTask());
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new OriginCommands(fixture.Device);
        valid = false;
        Assert.Equal("Source generation expired.", Assert.Throws<InvalidOperationException>(() => compiled.Record(commands)).Message);
        Assert.Equal(0, fixture.Records);
        commands.Dispose();
        valid = true;
    }

    /// <summary>Direct and retained generic batches retain the supplied runtime generation validator.</summary>
    /// <param name="retained">Whether to use the retained snapshot preparation path.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceGenerationIsRevalidatedAfterDirectPreparation(bool retained)
    {
        bool valid = true;
        using var fixture = new Fixture(() =>
        {
            if (!valid)
            {
                throw new InvalidOperationException("Source generation expired.");
            }
        });
        using var texture = new OriginTexture(fixture.Device);
        using IGraphicsTextureView view = texture.CreateView();
        var target = new RenderTarget2D { View = view };
        DrawList2D list = List().Commands;
        using var commands = new OriginCommands(fixture.Device);
        if (retained)
        {
            using var snapshot = new DrawingSnapshot2D(1, [new(default, list, System.Numerics.Matrix3x2.Identity, [], 1, 1)]);
            using PreparedDrawingFrame2D frame = fixture.Complete(fixture.Renderer.PrepareAsync(snapshot, target).AsTask());
            valid = false;
            Assert.Equal("Source generation expired.", Assert.Throws<InvalidOperationException>(() => frame.Record(commands)).Message);
            commands.Dispose();
            frame.AbortUnsubmitted();
        }
        else
        {
            using PreparedDraw2D prepared = fixture.Complete(fixture.Renderer.PrepareAsync(list, target).AsTask());
            valid = false;
            Assert.Equal("Source generation expired.", Assert.Throws<InvalidOperationException>(() => prepared.Record(commands)).Message);
            commands.Dispose();
        }

        Assert.Equal(0, fixture.Records);
        valid = true;
    }

    /// <summary>Structural commands require an explicitly built compositor, never implicit legacy shaders.</summary>
    /// <param name="backdrop">Whether the structural operation captures and replaces the backdrop.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingStructuralArtifactIsRejectedBeforeGraphMutation(bool backdrop)
    {
        using var fixture = new Fixture();
        using var graph = new Graph(fixture.Device);
        using var texture = new OriginTexture(fixture.Device);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        if (backdrop)
        {
            canvas.DrawBackdropEffect(new() { OutputBounds = new(0, 0, 4, 4), Effects = effects => effects.Source });
        }
        else
        {
            canvas.BeginLayer(new() { ContentBounds = new(0, 0, 4, 4) });
            canvas.FillRect(new(0, 0, 2, 2), new SRgba<float>(1, 0, 0, 1));
            canvas.EndLayer();
        }

        NotSupportedException error = Assert.Throws<NotSupportedException>(() => new RenderGraphRenderer2D(fixture.Renderer, canvas.End(), Target(graph, texture), new(graph)));
        Assert.Contains("structural artifact", error.Message, StringComparison.Ordinal);
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Empty(compiled.PassNames);
        Assert.Empty(fixture.Requests);
    }

    /// <summary>The graph borrows the exact source binding and waits on its whole-host preparation gate.</summary>
    /// <returns>The asynchronous gate verification.</returns>
    [Fact]
    public async Task SharedPreparationGateCancelsWithoutMutationAndAllowsRetryAsync()
    {
        using var fixture = new Fixture();
        using var graph = new Graph(fixture.Device);
        using var texture = new OriginTexture(fixture.Device);
        using var renderer = new RenderGraphRenderer2D(fixture.Renderer, List(), Target(graph, texture), new(graph));
        object binding = typeof(Renderer2D).GetProperty("PipelineBinding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Renderer)!;
        object coordinator = typeof(Renderer2D).GetField("_resources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fixture.Renderer)!;
        Assert.Same(coordinator, typeof(RenderGraphRenderer2D).GetField("_resources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer));
        Assert.Same(binding, typeof(RenderGraphRenderer2D).GetField("_pipelineBinding", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(renderer));
        MethodInfo acquire = binding.GetType().GetMethod("AcquirePreparationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using IDisposable held = await (ValueTask<IDisposable>)acquire.Invoke(binding, [CancellationToken.None])!;
        using var cancel = new CancellationTokenSource();
        Task<PreparedDrawingPasses2D> waiting = graph.Append2D(renderer, cancel.Token).AsTask();
        Assert.False(waiting.IsCompleted);
        Assert.Empty(fixture.Requests);
        cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => fixture.Complete(waiting));
        held.Dispose();
        using PreparedDrawingPasses2D prepared = fixture.Complete(graph.Append2D(renderer).AsTask());
        Assert.Single(fixture.Requests);
    }

    /// <summary>Readback readiness is rejected while the graph still has no passes or prepared targets.</summary>
    [Fact]
    public void ReadbackPipelineIsRejectedBeforeGraphMutation()
    {
        using var fixture = new Fixture(readback: true);
        using var graph = new Graph(fixture.Device);
        using var texture = new OriginTexture(fixture.Device);
        Assert.Throws<NotSupportedException>(() => new RenderGraphRenderer2D(fixture.Renderer, List(), Target(graph, texture), new(graph)));
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Empty(compiled.PassNames);
        Assert.Empty(fixture.Requests);
    }

    private static RenderGraphDrawList2D List()
    {
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 2, 2), new SRgba<float>(1, 0, 0, 1));
        canvas.FillCircle(new(2, 2), 1, new SRgba<float>(0, 1, 0, 1));
        canvas.FillRect(new(1, 1, 2, 2), new SRgba<float>(0, 0, 1, 1));
        return canvas.End();
    }

    private static RenderTargetBinding2D Target(Graph graph, OriginTexture texture) => new()
    {
        Texture = graph.ImportTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite)),
        Description = new() { Width = texture.Width, Height = texture.Height, Format = texture.Format, Usage = texture.Usage },
    };

    private sealed class Fixture : IDisposable
    {
        private readonly ExecutionRuntime _runtime = new([]);
        private readonly GraphicsPipelineBuildHost _build;

        internal Fixture(Action? validate = null, bool readback = false)
        {
            _runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
            Pump(_runtime.StartAsync().AsTask());
            var policy = new PipelinePolicy2D(new(1024 * 1024, 1024 * 1024, 1024 * 1024, 3), DrawingArgumentTableMode.Auto, new());
            _build = new(_runtime, Device, new EmptyArtifacts(), policy.Execution);
            var g = new PipelineGraph<RenderRequest2D>();
            Port<RenderRequest2D> observed = g.Stage(new Observe(this), g.Input);
            Port<Device<TargetV1>> target = g.Stage(RenderFields2D.Target(), observed);
            if (readback)
            {
                _ = g.Stage(RenderFields2D.Frame(), observed).Then(Transfers.Upload<FrameV1>()).Then(Transfers.Download<FrameV1>());
            }

            Pipeline = _build.Build(g.Complete(g.Stage(new Endpoint(this), target)));
            Renderer = new(Device, Pipeline, new(true, true, true, true, true, true, true, true, true, true, true), policy, validateResources: validate);
        }

        internal ProfilingDevice Device { get; } = new();

        internal List<RenderRequest2D> Requests { get; } = [];

        internal IGraphicsTextureView? PreparedTarget { get; set; }

        internal int Records { get; set; }

        internal IPipeline<RenderRequest2D, Device<TargetWritesV1>> Pipeline { get; }

        internal Renderer2D Renderer { get; }

        public void Dispose()
        {
            Renderer.Dispose();
            Pipeline.Dispose();
            _build.Dispose();
            Pump(_runtime.StopAsync().AsTask());
        }

        internal T Complete<T>(Task<T> task)
        {
            Pump(task);
            return task.GetAwaiter().GetResult();
        }

        private void Pump(Task task)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                _runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
                _runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
                if (timeout.Elapsed > TimeSpan.FromSeconds(15))
                {
                    throw new TimeoutException("Generic graph preparation did not complete.");
                }
            }

            task.GetAwaiter().GetResult();
        }
    }

    private sealed class Observe(Fixture fixture) : IStage<RenderRequest2D, RenderRequest2D>, IStageFactory<RenderRequest2D, RenderRequest2D>, IStageProcessor<RenderRequest2D, RenderRequest2D>
    {
        public IStageFactory<RenderRequest2D, RenderRequest2D> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(256, []);

        public IStageProcessor<RenderRequest2D, RenderRequest2D> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<RenderRequest2D>> PrepareAsync(StagePreparationContext context, ValueLease<RenderRequest2D> a, CancellationToken cancellationToken)
        {
            fixture.Requests.Add(context.Read(a));
            return ValueTask.FromResult(context.Forward(a));
        }

        public void Dispose()
        {
        }
    }

    private sealed class Endpoint(Fixture fixture) : IStage<Device<TargetV1>, Device<TargetWritesV1>>, IStageFactory<Device<TargetV1>, Device<TargetWritesV1>>, IStageProcessor<Device<TargetV1>, Device<TargetWritesV1>>
    {
        public IStageFactory<Device<TargetV1>, Device<TargetWritesV1>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(256, [new GraphicsStageRequirements(GraphicsExecutionDomain.Raster, 0, [], [new(0, ResourceAccess.ColorRead | ResourceAccess.ColorWrite)])]);

        public IStageProcessor<Device<TargetV1>, Device<TargetWritesV1>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Device<TargetWritesV1>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<TargetV1>> a, CancellationToken cancellationToken)
        {
            GraphicsStagePreparationContext gpu = ((IGraphicsStageCapabilities)context.Capabilities).Preparation;
            DeviceBindings<TargetV1> target = gpu.GetDeviceBindings(a);
            fixture.PreparedTarget = target.GetTexture(0);
            ValueLease<Device<TargetWritesV1>> output = gpu.AllocateDeviceOutput<TargetWritesV1>(new(1, target.Extent.Metadata));
            gpu.Encode(output, _ => fixture.Records++);
            return ValueTask.FromResult(output);
        }

        public void Dispose()
        {
        }
    }

    private sealed class EmptyArtifacts : IStageArtifactProvider
    {
        public StageArtifactDescription Describe(StageArtifactKey key, IGraphicDevice device) => throw new InvalidOperationException("No artifacts were declared.");

        public StageArtifactLease Acquire(StageArtifactKey key, StageArtifactBuildContext context) => throw new InvalidOperationException("No artifacts were declared.");
    }

    private sealed class OriginCommands(IGraphicDevice device) : ProfilingCommands, IGraphicsNativeOrigin
    {
        public IGraphicDevice Device => device;

        public object AllocationIdentity => this;

        public void ValidateOrigin() => ObjectDisposedException.ThrowIf(State == CommandBufferState.Disposed, this);
    }

    private sealed class OriginTexture(IGraphicDevice device) : IGraphicsTexture, IGraphicsNativeOrigin
    {
        private bool _disposed;

        public IGraphicDevice Device => device;

        public object AllocationIdentity => this;

        public uint Width => 8;

        public uint Height => 8;

        public uint MipLevels => 1;

        public uint ArrayLayers => 1;

        public TextureFormat Format => TextureFormat.Rgba16Float;

        public TextureUsage Usage => TextureUsage.RenderAttachment | TextureUsage.Sampled;

        public (uint Width, uint Height) GetMipSize(uint mipLevel) => (Width, Height);

        public IGraphicsTextureView CreateView(TextureViewDesc? desc = null) => new OriginView(this);

        public void ValidateOrigin() => ObjectDisposedException.ThrowIf(_disposed, this);

        public void Dispose() => _disposed = true;
    }

    private sealed class OriginView(OriginTexture texture) : IGraphicsTextureView, IGraphicsNativeOrigin
    {
        public IGraphicsTexture Texture => texture;

        public IGraphicDevice Device => texture.Device;

        public object AllocationIdentity => texture.AllocationIdentity;

        public TextureViewInfo Info => new() { Format = texture.Format, Dimension = TextureViewDimension.D2, BaseMipLevel = 0, MipLevelCount = 1, BaseArrayLayer = 0, ArrayLayerCount = 1 };

        public void ValidateOrigin() => texture.ValidateOrigin();

        public void Dispose()
        {
        }
    }
}
