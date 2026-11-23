using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Evaluates canonical sixteen-sample clip into independent native storage.</summary>
public sealed class ComputeClip2D : IStage<Device<SceneV1>, Device<FrameV1>, Device<Clip16V1>>
{
    internal ComputeClip2D(SamplePolicy2D samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        samples.Validate();
        Factory = new Implementation(null);
    }

    /// <summary>Gets the executable factory.</summary>
    public IStageFactory<Device<SceneV1>, Device<FrameV1>, Device<Clip16V1>> Factory { get; }

    private sealed class Implementation(StageArtifactLease? artifact) : IStageFactory<Device<SceneV1>, Device<FrameV1>, Device<Clip16V1>>, IStageProcessor<Device<SceneV1>, Device<FrameV1>, Device<Clip16V1>>
    {
        public StageRequirements Describe(StageInputDescriptions inputs) => GenericGpuPreparation2D.Describe<ComputeClip2D>(inputs, GraphicsExecutionDomain.Compute, 4, resources: false);

        public IStageProcessor<Device<SceneV1>, Device<FrameV1>, Device<Clip16V1>> Create(StageBuildContext context) => new Implementation(GenericGpuPreparation2D.Artifact<ComputeClip2D>(context));

        public ValueTask<ValueLease<Device<Clip16V1>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<SceneV1>> a, ValueLease<Device<FrameV1>> b, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphicsStagePreparationContext gpu = GenericGpuPreparation2D.Preparation(context);
            DeviceBindings<SceneV1> scene = gpu.GetDeviceBindings(a);
            DeviceBindings<FrameV1> frame = gpu.GetDeviceBindings(b);
            GenericGpuPreparation2D.Match(scene.Extent, frame.Extent);
            long count = GenericGpuPreparation2D.Count(scene.Extent);
            ValueLease<Device<Clip16V1>> output = gpu.AllocateDeviceOutput<Clip16V1>(new(count, scene.Extent.Metadata));
            BufferSlice<float> destination = gpu.GetDeviceBindings(output).GetBuffer<float>(0);
            IArgumentTable table = GenericGpuPreparation2D.Table(gpu);
            GenericArguments2D args = GenericGpuPreparation2D.Arguments(scene.Extent, table, scene.GetBuffer<Vector4>(0));
            args.Clip = table.WriteBuffer(3, destination, ShaderBufferAccess.ReadWrite);
            GenericGpuPreparation2D.Compute(gpu, artifact!, table, args, count, null, record => gpu.Encode(output, stageRecord =>
            {
                record(stageRecord);
                stageRecord.Commands.Barrier(new BufferBarrierDesc<float>
                {
                    Buffer = destination,
                    Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite),
                    After = new(PipelineStage.FragmentShader | PipelineStage.Copy, ResourceAccess.ShaderRead | ResourceAccess.CopyRead),
                });
            }));
            return ValueTask.FromResult(output);
        }

        public void Dispose()
        {
        }
    }
}
