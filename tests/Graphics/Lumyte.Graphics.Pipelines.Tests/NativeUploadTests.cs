using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Checks actual native transfer bytes and record-only ownership.</summary>
public sealed partial class NativeUploadTests
{
    /// <summary>Uploads through the generic graph and records an actual native copy exactly once.</summary>
    [NativeFact]
    public void VulkanUploadRecordsActualCanonicalBytes()
    {
        using var device = VulkanDevice.Create();
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        var graph = new PipelineGraph<int>();
        Port<Device<Words>> output = graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>()).Then(Transfers.Passthrough<Words>());
        using var host = new GraphicsPipelineBuildHost(runtime, device, new NoArtifacts(), new GraphicsPipelinePolicy(16384, 64, 64, 2));
        using IPipeline<int, Device<Words>> pipeline = host.Build(graph.Complete(output));
        Task<PipelineResult<Device<Words>>> pending = pipeline.PrepareAsync(4).AsTask();
        Pump(runtime, pending);
        using PipelineResult<Device<Words>> result = pending.Result;
        using GraphicsPipelineRecordPlan plan = result.AcquireRecordPlan(pipeline, new InlineContext());
        Assert.Throws<InvalidOperationException>(() => result.AcquireRecordPlan(pipeline, new InlineContext()));
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new CommandBufferDesc());
        plan.Record(commands);
        Assert.Equal(CommandBufferState.Recording, commands.State);
        Assert.Throws<InvalidOperationException>(() => plan.Record(commands));
        BufferSlice<uint> source = result.Value.Value!.GetBuffer<uint>();
        commands.CopyBuffer(source, readback.SliceElements(0, 4));
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        submission.WaitAsync().AsTask().GetAwaiter().GetResult();
        readback.MapAsync().AsTask().GetAwaiter().GetResult();
        try
        {
            uint[] values = new uint[4];
            readback.CopyTo(values);
            Assert.Equal(new uint[] { 17, 29, 41, 53 }, values);
        }
        finally
        {
            readback.Unmap();
        }

        commands.Dispose();
        plan.Dispose();
        result.Dispose();
        Pump(runtime, runtime.StopAsync().AsTask());
    }

    /// <summary>Executes real upload/readback, CPU continuation, second upload/readback and final host result.</summary>
    [NativeFact]
    public void VulkanSegmentedExecutionResumesActualHostBytes()
    {
        using var device = VulkanDevice.Create();
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        var graph = new PipelineGraph<int>();
        Port<Host<Words>> downloaded = graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>()).Then(Transfers.Download<Words>());
        Port<int> output = downloaded.Then(new IncrementWords()).Then(Transfers.Upload<Words>()).Then(Transfers.Download<Words>()).Then(new SumWords());
        using var build = new GraphicsPipelineBuildHost(runtime, device, new NoArtifacts(), new GraphicsPipelinePolicy(32768, 128, 128, 4));
        using IPipeline<int, int> pipeline = build.Build(graph.Complete(output));
        Task<PipelineResult<int>> pending = pipeline.PrepareAsync(4).AsTask();
        Pump(runtime, pending);
        using PipelineResult<int> result = pending.GetAwaiter().GetResult();
        var context = new InlineContext();
        Assert.Throws<NotSupportedException>(() => result.AcquireRecordPlan(pipeline, context));
        Assert.Throws<InvalidOperationException>(() => result.Value);
        using GraphicsPipelineExecution execution = result.AcquireExecution(pipeline, context);
        using var host = new GraphicsPipelineExecutionHost(pipeline, context);
        Task completed = host.ExecuteAsync(execution).AsTask();
        try
        {
            Pump(runtime, completed);
        }
        finally
        {
            Pump(runtime, host.DrainAsync().AsTask());
        }

        Assert.Equal(540, result.Value);
        Assert.Equal(0, host.RecoveryState.QuarantinedEpochs);
        Assert.Equal(0, host.RecoveryState.PendingCleanupOwners);
        execution.Dispose();
        result.Dispose();
        Pump(runtime, runtime.StopAsync().AsTask());
    }

    private static void Pump(ExecutionRuntime runtime, Task task)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
            runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            if (timeout.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new TimeoutException();
            }

            Thread.Yield();
        }

        task.GetAwaiter().GetResult();
    }

    private sealed class Words : ISchema
    {
        public static SchemaDescriptor Descriptor { get; } = new(1, typeof(uint), 4, new Dictionary<string, object>());
    }

    private sealed class Producer : IStage<int, Host<Words>>, IStageFactory<int, Host<Words>>, IStageProcessor<int, Host<Words>>
    {
        public IStageFactory<int, Host<Words>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(216, []);

        public IStageProcessor<int, Host<Words>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<Words>>> PrepareAsync(StagePreparationContext context, ValueLease<int> a, CancellationToken cancellationToken)
        {
            SchemaData<Words> data = context.CopyHostData<Words, uint>([17, 29, 41, 53], new SchemaExtent(4, new Dictionary<string, object>()));
            return ValueTask.FromResult(context.PublishHost(data));
        }

        public void Dispose()
        {
        }
    }

    private sealed class IncrementWords : IStage<Host<Words>, Host<Words>>, IStageFactory<Host<Words>, Host<Words>>, IStageProcessor<Host<Words>, Host<Words>>
    {
        public IStageFactory<Host<Words>, Host<Words>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(4096, []) { OutputMaxElements = 16 };

        public IStageProcessor<Host<Words>, Host<Words>> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<Host<Words>>> PrepareAsync(StagePreparationContext context, ValueLease<Host<Words>> a, CancellationToken cancellationToken)
        {
            SchemaData<Words> input = context.ReadHost(a);
            if (input.Extent.ElementCount > 16)
            {
                throw new InvalidOperationException("Copied words exceed the explicit finite input ceiling.");
            }

            uint[] values = input.ReadElements<uint>().ToArray();
            for (int i = 0; i < values.Length; i++)
            {
                values[i] += 100;
            }

            SchemaData<Words> data = context.CopyHostData<Words, uint>(values, new SchemaExtent(values.Length, new Dictionary<string, object>()));
            return ValueTask.FromResult(context.PublishHost(data));
        }

        public void Dispose()
        {
        }
    }

    private sealed class SumWords : IStage<Host<Words>, int>, IStageFactory<Host<Words>, int>, IStageProcessor<Host<Words>, int>
    {
        public IStageFactory<Host<Words>, int> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(68, []);

        public IStageProcessor<Host<Words>, int> Create(StageBuildContext context) => this;

        public ValueTask<ValueLease<int>> PrepareAsync(StagePreparationContext context, ValueLease<Host<Words>> a, CancellationToken cancellationToken)
        {
            int sum = 0;
            foreach (uint value in context.ReadHost(a).ReadElements<uint>())
            {
                sum = checked(sum + (int)value);
            }

            return ValueTask.FromResult(context.Publish(sum));
        }

        public void Dispose()
        {
        }
    }

    private sealed class NoArtifacts : IStageArtifactProvider
    {
        public StageArtifactDescription Describe(StageArtifactKey key, IGraphicDevice device) => throw new InvalidOperationException("No shader artifact is required for a transfer.");

        public StageArtifactLease Acquire(StageArtifactKey key, StageArtifactBuildContext context) => throw new InvalidOperationException("No shader artifact is required for a transfer.");
    }

    private sealed class InlineContext : IGraphicsOwnerContext
    {
        public ValueTask InvokeAsync(Func<ValueTask> operation) => operation();
    }
}
