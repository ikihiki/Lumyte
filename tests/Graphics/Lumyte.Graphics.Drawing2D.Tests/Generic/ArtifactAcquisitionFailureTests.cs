using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Tests ownership on real embedded-artifact acquisition, not GPU execution correctness.</summary>
public sealed class ArtifactAcquisitionFailureTests
{
    /// <summary>Preserves both failures and retains unpublished native ownership for a later cleanup retry.</summary>
    /// <param name="checkRetryFirst">Checks retry ownership first, or primary error preservation first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPipelineCreationPreservesPrimaryAndRetriesShaderCleanup(bool checkRetryFirst)
    {
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        var device = new FaultDevice();
        var stage = new ArtifactStage();
        var graph = new PipelineGraph<Device<SceneV1>>();
        PipelineDefinition<Device<SceneV1>, Device<SceneV1>> definition = graph.Complete(graph.Stage(stage, graph.Input));
        using var build = new GraphicsPipelineBuildHost(runtime, device, GenericGpuArtifactProvider2D.CreateEmbedded(), new(1024, 0, 0, 1));

        Exception? failure = Record.Exception(() => build.Build(definition));
        Assert.Equal(1, device.ShaderCreations);
        Assert.Equal(1, device.ComputePipelineCreations);
        Assert.Equal(0, device.UnexpectedCalls);
        Assert.Equal(0, stage.ProcessorCreations);
        Assert.Equal(1, device.Shader.DisposeAttempts);
        Assert.Equal(0, device.Shader.SuccessfulDisposals);

        if (!checkRetryFirst)
        {
            AssertPreservedErrors();
        }

        build.Dispose();
        Assert.Equal(2, device.Shader.DisposeAttempts);
        Assert.Equal(1, device.Shader.SuccessfulDisposals);
        build.Dispose();
        Assert.Equal(2, device.Shader.DisposeAttempts);
        Assert.Equal(0, device.UnexpectedCalls);
        Assert.Equal(0, stage.ProcessorCreations);

        AssertPreservedErrors();

        void AssertPreservedErrors()
        {
            AggregateException aggregate = Assert.IsType<AggregateException>(failure);
            IReadOnlyCollection<Exception> errors = aggregate.Flatten().InnerExceptions;
            Assert.Equal(2, errors.Count);
            Assert.Contains(errors, error => ReferenceEquals(error, device.PrimaryFailure));
            Assert.Contains(errors, error => ReferenceEquals(error, device.CleanupFailure));
        }
    }

    private sealed class ArtifactStage : IStage<Device<SceneV1>, Device<SceneV1>>, IStageFactory<Device<SceneV1>, Device<SceneV1>>
    {
        public int ProcessorCreations { get; private set; }

        public IStageFactory<Device<SceneV1>, Device<SceneV1>> Factory => this;

        public StageRequirements Describe(StageInputDescriptions inputs) => new(0, [new GraphicsStageRequirements(GraphicsExecutionDomain.Compute, 0, [new StageArtifactKey(typeof(ComputeCoverage2D), 1, 0)], [new StageInputAccess(0, ResourceAccess.ShaderRead)])]);

        public IStageProcessor<Device<SceneV1>, Device<SceneV1>> Create(StageBuildContext context)
        {
            ProcessorCreations++;
            throw new InvalidOperationException("Processors must not be created after artifact acquisition fails.");
        }
    }

    private sealed class FaultShader(ShaderArtifact artifact, Exception cleanupFailure) : IGraphicsShader
    {
        public ShaderArtifact Artifact { get; } = artifact;

        public int DisposeAttempts { get; private set; }

        public int SuccessfulDisposals { get; private set; }

        public void Dispose()
        {
            DisposeAttempts++;
            if (DisposeAttempts == 1)
            {
                throw cleanupFailure;
            }

            SuccessfulDisposals++;
        }
    }

    // Deliberately provides no command recording, queue, submission, or native execution.
    // Only the real provider's shader acquisition and failing pipeline creation are supported.
    private sealed class FaultDevice : IGraphicDevice
    {
        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost
        {
            add
            {
            }

            remove
            {
            }
        }

        public InvalidOperationException PrimaryFailure { get; } = new("Injected compute pipeline creation failure.");

        public InvalidOperationException CleanupFailure { get; } = new("Injected first shader disposal failure.");

        public FaultShader Shader { get; private set; } = null!;

        public int ShaderCreations { get; private set; }

        public int ComputePipelineCreations { get; private set; }

        public int UnexpectedCalls { get; private set; }

        public GraphicsDeviceStatus Status => GraphicsDeviceStatus.Ready;

        public GraphicsDeviceLoss? Loss => null;

        public DeviceCaps Caps { get; } = new() { ShaderTarget = ShaderTarget.SpirV, MaxComputeInvocationsPerWorkgroup = 256 };

        public IGraphicsQueue Queue => throw Unexpected();

        public IGraphicsShader CreateShader(ShaderArtifact artifact)
        {
            ShaderCreations++;
            Shader = new FaultShader(artifact, CleanupFailure);
            return Shader;
        }

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc)
        {
            ComputePipelineCreations++;
            Assert.Same(Shader, desc.ComputeShader);
            throw PrimaryFailure;
        }

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => throw Unexpected();

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged => throw Unexpected();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged => throw Unexpected();

        public IGraphicsTexture CreateTexture(TextureDesc desc) => throw Unexpected();

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => throw Unexpected();

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => throw Unexpected();

        public IGraphicsSemaphore CreateSemaphore() => throw Unexpected();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => throw Unexpected();

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => throw Unexpected();

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => throw Unexpected();

        public IMeshPipeline CreateMeshPipeline(MeshPipelineDesc desc) => throw Unexpected();

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : struct, IShaderArguments => throw Unexpected();

        private InvalidOperationException Unexpected()
        {
            UnexpectedCalls++;
            return new("Unexpected device operation in acquisition-only regression.");
        }
    }
}
