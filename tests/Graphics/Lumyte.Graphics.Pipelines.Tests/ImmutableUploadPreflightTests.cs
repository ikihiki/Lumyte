using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Pipelines.Tests;

/// <summary>Verifies immutable storage contracts before building native artifacts or processors.</summary>
public sealed partial class NativeUploadTests
{
    /// <summary>A writable consumer cannot mutate a completed shared canonical upload, even through passthrough.</summary>
    /// <param name="copy">Whether the illegal write is a transfer access rather than shader storage access.</param>
    [NativeTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Execution", "Gpu")]
    public void ImmutableUploadWriterRejectsBeforeProcessorCreation(bool copy)
    {
        using var device = VulkanDevice.Create();
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new());
        Pump(runtime, runtime.StartAsync().AsTask());
        try
        {
            var graph = new PipelineGraph<int>();
            Port<Device<Words>> words = graph.Stage(new Producer(), graph.Input).Then(Transfers.Upload<Words>()).Then(Transfers.Passthrough<Words>());
            var policy = new GraphicsPipelinePolicy(65536, 256, 256, 4) { ImmutableUploads = new(1024, 8) };
            using var build = new GraphicsPipelineBuildHost(runtime, device, new NoArtifacts(), policy);
            Assert.Throws<NotSupportedException>(() => build.Build(graph.Complete(graph.Stage(new IllegalWriter(copy), words))));
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    private sealed class IllegalWriter(bool copy) : IStage<Device<Words>, Device<Words>>, IStageFactory<Device<Words>, Device<Words>>
    {
        public IStageFactory<Device<Words>, Device<Words>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(0, [new GraphicsStageRequirements(GraphicsExecutionDomain.Compute, 16, [], [new(0, copy ? ResourceAccess.CopyWrite : ResourceAccess.ShaderWrite)])]);

        public IStageProcessor<Device<Words>, Device<Words>> Create(StageBuildContext context) => throw new InvalidOperationException("Preflight must reject before processor creation.");
    }
}
