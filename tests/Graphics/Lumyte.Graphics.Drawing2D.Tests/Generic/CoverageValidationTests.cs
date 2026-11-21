using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises real canonical CPU stage behavior without native GPU execution.</summary>
public sealed class CoverageValidationTests
{
    /// <summary>Finite Coverage Checks Source Grid Extent And Range.</summary>
    /// <param name="defect">Zero for valid data; otherwise the deliberately invalid source, range, or extent.</param>
    /// <returns>The completed validation regression check.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task FiniteCoverageChecksSourceGridExtentAndRangeAsync(int defect)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        void Pump(Task task)
        {
            for (int i = 0; !task.IsCompleted && i < 100000; i++)
            {
                runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
                runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
            }

            Assert.True(task.IsCompleted);
            task.GetAwaiter().GetResult();
        }

        Pump(runtime.StartAsync().AsTask());
        try
        {
            var graph = new PipelineGraph<RenderRequest2D>();
            Port<Host<Coverage16V1>> coverage = graph.Stage(new Producer(defect), graph.Input);
            Port<Host<FrameV1>> frame = graph.Stage(RenderFields2D.Frame(), graph.Input);
            Port<Host<Coverage16V1>> validated = graph.Stage(CpuStages2D.ValidateCoverage(new(16, 1)), coverage, frame);
            using var build = new PipelineBuildHost(runtime, new(8, 16 * 1024 * 1024, 1));
            using IPipeline<RenderRequest2D, Host<Coverage16V1>> pipeline = build.Build(graph.Complete(validated));
            var target = new DrawingTargetInfo2D { Description = new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment } };
            var state = new DrawingState2D(System.Numerics.Matrix3x2.Identity, ClipStack2D.Empty);
            var request = new RenderRequest2D(target, [new DrawPaintGraphCommand2D(PaintGraph2D.Solid(new(1, 0, 0, 1)), state)], Guid.NewGuid());
            Task<PipelineResult<Host<Coverage16V1>>> pending = pipeline.PrepareAsync(request).AsTask();
            if (defect == 0)
            {
                Pump(pending);
                (await pending).Dispose();
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => Pump(pending));
            }
        }
        finally
        {
            Pump(runtime.StopAsync().AsTask());
        }
    }

    private sealed class Producer(int defect) : IStage<RenderRequest2D, Host<Coverage16V1>>, IStageFactory<RenderRequest2D, Host<Coverage16V1>>, IStageProcessor<RenderRequest2D, Host<Coverage16V1>>
    {
        public IStageFactory<RenderRequest2D, Host<Coverage16V1>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(1024, []);

        public IStageProcessor<RenderRequest2D, Host<Coverage16V1>> Create(StageBuildContext context) => new Producer(defect);

        public ValueTask<ValueLease<Host<Coverage16V1>>> PrepareAsync(StagePreparationContext context, ValueLease<RenderRequest2D> a, CancellationToken cancellationToken)
        {
            RenderRequest2D request = context.Read(a);
            Dictionary<string, object> metadata = request.Metadata();
            if (defect == 1)
            {
                metadata["source"] = Guid.NewGuid();
            }

            float[] values = [.. Enumerable.Repeat(0.5f, defect == 4 ? 15 : 16)];
            if (defect == 2)
            {
                values[3] = float.NaN;
            }

            if (defect == 3)
            {
                values[3] = 1.01f;
            }

            return ValueTask.FromResult(context.PublishHost(context.CopyHostData<Coverage16V1, float>(values, new(values.Length, metadata))));
        }

        public void Dispose()
        {
        }
    }
}
