using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Pipelines;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Pipelines.RenderGraph.Tests;

internal sealed class NativeCopyFixture : IDisposable
{
    private readonly GraphicsPipelineBuildHost _build;
    private readonly IPipeline<Request, Device<Writes>> _pipeline;
    private readonly PipelineResult<Device<Writes>> _result;

    internal NativeCopyFixture()
    {
        Device = VulkanDevice.Create();
        Source = Device.CreateBuffer<uint>(new BufferDesc<uint> { Count = 8, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        Destination = Device.CreateBuffer<uint>(new BufferDesc<uint> { Count = 8, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        Source.MapAsync().AsTask().GetAwaiter().GetResult();
        Source.CopyFrom([10, 20, 30, 40, 50, 60, 70, 80]);
        Source.Unmap();
        Runtime = new ExecutionRuntime([]);
        Runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(Runtime.StartAsync().AsTask());
        var definition = new PipelineGraph<Request>();
        Port<Device<Words>> source = definition.Stage(new Borrow(false), definition.Input);
        Port<Device<Words>> destination = definition.Stage(new Borrow(true), definition.Input);
        Port<Device<Writes>> copied = definition.Stage(new Copy(), source, destination);
        _build = new GraphicsPipelineBuildHost(Runtime, Device, new NoArtifacts(), new GraphicsPipelinePolicy(16384, 128, 128, 1));
        _pipeline = _build.Build(definition.Complete(copied));
        Task<PipelineResult<Device<Writes>>> pending = _pipeline.PrepareAsync(new Request(Source, Destination, Leases)).AsTask();
        Pump(pending);
        _result = pending.GetAwaiter().GetResult();
        Plan = _result.AcquireRecordPlan(_pipeline, new InlineContext());
        Assert.Equal(2, Plan.Resources.Count);
        Assert.Equal(2, Leases.Active);
    }

    internal VulkanDevice Device { get; }

    internal IGraphicsBuffer<uint> Source { get; }

    internal IGraphicsBuffer<uint> Destination { get; }

    internal ExecutionRuntime Runtime { get; }

    internal LeaseCounter Leases { get; } = new();

    internal GraphicsPipelineRecordPlan Plan { get; }

    public void Dispose()
    {
        Plan.Dispose();
        _result.Dispose();
        _pipeline.Dispose();
        _build.Dispose();
        Pump(Runtime.StopAsync().AsTask());
        Destination.Dispose();
        Source.Dispose();
        Device.Dispose();
    }

    internal void ReturnOriginalOwners()
    {
        Plan.Dispose();
        _result.Dispose();
        _pipeline.Dispose();
    }

    internal PipelineGraphBindings Bind(Graph graph)
    {
        RenderGraphBuffer<uint> source = graph.ImportBuffer(Source, new BarrierScope(PipelineStage.Host, ResourceAccess.HostWrite));
        RenderGraphBuffer<uint> destination = graph.ImportBuffer(Destination, new BarrierScope(PipelineStage.Host, ResourceAccess.HostRead));
        return new PipelineGraphBindings([], [
            PipelineGraphBufferBinding.Create(Plan.Resources.GetSlot(0), source, Source, new BarrierScope(PipelineStage.ComputeShader | PipelineStage.Copy, ResourceAccess.CopyRead)),
            PipelineGraphBufferBinding.Create(Plan.Resources.GetSlot(1), destination, Destination, new BarrierScope(PipelineStage.ComputeShader | PipelineStage.Copy, ResourceAccess.CopyWrite)),
        ]);
    }

    internal void Execute(CompiledRenderGraph graph)
    {
        using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new CommandBufferDesc());
        graph.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = Device.Queue.Submit([commands]);
        submission.WaitAsync().AsTask().GetAwaiter().GetResult();
        commands.Dispose();
    }

    internal void VerifyCopiedBytes()
    {
        Destination.MapAsync().AsTask().GetAwaiter().GetResult();
        try
        {
            uint[] values = new uint[8];
            Destination.CopyTo(values);
            Assert.Equal(new uint[] { 30, 40, 50, 60 }, values[1..5]);
        }
        finally
        {
            Destination.Unmap();
        }
    }

    private void Pump(Task task)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            Runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            if (timeout.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException();
            }

            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    internal sealed class LeaseCounter
    {
        internal int Active { get; private set; }

        internal IDisposable Acquire()
        {
            Active++;
            return new Use(this);
        }

        private sealed class Use(LeaseCounter owner) : IDisposable
        {
            private bool _returned;

            public void Dispose()
            {
                if (!_returned)
                {
                    owner.Active--;
                    _returned = true;
                }
            }
        }
    }

    private sealed record Request(IGraphicsBuffer<uint> Source, IGraphicsBuffer<uint> Destination, LeaseCounter Leases);

    private sealed class Words : ISchema
    {
        public static SchemaDescriptor Descriptor { get; } = new(1, typeof(uint), 4, new Dictionary<string, object>());
    }

    private sealed class Writes : ISchema
    {
        public static SchemaDescriptor Descriptor { get; } = new(1, typeof(uint), 4, new Dictionary<string, object> { ["dependencyOnly"] = true });
    }

    private sealed class Borrow(bool destination) : IStage<Request, Device<Words>>, IStageFactory<Request, Device<Words>>, IStageProcessor<Request, Device<Words>>
    {
        public IStageFactory<Request, Device<Words>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(1024, []) { OutputMaxElements = 4 };

        public IStageProcessor<Request, Device<Words>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Device<Words>>> PrepareAsync(StagePreparationContext context, ValueLease<Request> a, CancellationToken cancellationToken)
        {
            Request request = context.Read(a);
            IDisposable lease = request.Leases.Acquire();
            try
            {
                var graphics = (IGraphicsStageCapabilities)context.Capabilities;
                BufferSlice<uint> range = destination ? request.Destination.SliceElements(1, 4) : request.Source.SliceElements(2, 4);
                return ValueTask.FromResult(graphics.Preparation.BorrowBuffer<Words, uint>(range, new SchemaExtent(4, new Dictionary<string, object>()), lease));
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
        }
    }

    private sealed class Copy : IStage<Device<Words>, Device<Words>, Device<Writes>>, IStageFactory<Device<Words>, Device<Words>, Device<Writes>>, IStageProcessor<Device<Words>, Device<Words>, Device<Writes>>
    {
        public IStageFactory<Device<Words>, Device<Words>, Device<Writes>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs)
            => new(4096, [new GraphicsStageRequirements(GraphicsExecutionDomain.Compute, 0, [], [new StageInputAccess(0, ResourceAccess.CopyRead), new StageInputAccess(1, ResourceAccess.CopyWrite)])]);

        public IStageProcessor<Device<Words>, Device<Words>, Device<Writes>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Device<Writes>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<Words>> a, ValueLease<Device<Words>> b, CancellationToken cancellationToken)
        {
            var graphics = (IGraphicsStageCapabilities)context.Capabilities;
            GraphicsStagePreparationContext gpu = graphics.Preparation;
            BufferSlice<uint> source = gpu.GetDeviceBindings(a).GetBuffer<uint>(0);
            BufferSlice<uint> destination = gpu.GetDeviceBindings(b).GetBuffer<uint>(0);
            ValueLease<Device<Writes>> output = gpu.AllocateDeviceOutput<Writes>(new SchemaExtent(1, new Dictionary<string, object>()));
            gpu.Encode(output, record => record.Commands.CopyBuffer(source, destination));
            return ValueTask.FromResult(output);
        }

        public void Dispose()
        {
        }
    }

    private sealed class InlineContext : IGraphicsOwnerContext
    {
        public ValueTask InvokeAsync(Func<ValueTask> operation) => operation();
    }

    private sealed class NoArtifacts : IStageArtifactProvider
    {
        public StageArtifactDescription Describe(StageArtifactKey key, IGraphicDevice device) => throw new InvalidOperationException("The native copy uses no shader artifact.");

        public StageArtifactLease Acquire(StageArtifactKey key, StageArtifactBuildContext context) => throw new InvalidOperationException("The native copy uses no shader artifact.");
    }
}
