using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Evaluates canonical sixteen-sample paint into independent native storage.</summary>
public sealed class ComputePaint2D : IStage<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>>
{
    internal ComputePaint2D(SamplePolicy2D samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        samples.Validate();
        Factory = new Implementation(null);
    }

    /// <summary>Gets the executable factory.</summary>
    public IStageFactory<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>> Factory { get; }

    private sealed class Implementation(StageArtifactLease? artifact) : IStageFactory<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>>, IStageProcessor<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>>
    {
        public StageRequirements Describe(StageInputDescriptions inputs) => GenericGpuPreparation2D.Describe<ComputePaint2D>(inputs, GraphicsExecutionDomain.Compute, 16, resources: true);

        public IStageProcessor<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>> Create(StageBuildContext context) => new Implementation(GenericGpuPreparation2D.Artifact<ComputePaint2D>(context));

        public async ValueTask<ValueLease<Device<Paint16V1>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<SceneV1>> a, ValueLease<Device<ResourceViewsV1>> b, ValueLease<Device<FrameV1>> c, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphicsStagePreparationContext gpu = GenericGpuPreparation2D.Preparation(context);
            DeviceBindings<SceneV1> scene = gpu.GetDeviceBindings(a);
            DeviceBindings<FrameV1> frame = gpu.GetDeviceBindings(c);
            GenericGpuPreparation2D.Match(scene.Extent, frame.Extent);
            long count = GenericGpuPreparation2D.Count(scene.Extent);
            DeviceBindings<ResourceViewsV1> views = gpu.GetDeviceBindings(b);
            GenericGpuPreparation2D.Match(scene.Extent, views.Extent);
            ValueLease<Device<Paint16V1>> output = gpu.AllocateDeviceOutput<Paint16V1>(new(count, scene.Extent.Metadata));
            BufferSlice<Vector4> destination = gpu.GetDeviceBindings(output).GetBuffer<Vector4>(0);
            IArgumentTable table = GenericGpuPreparation2D.Table(gpu, GenericGpuPreparation2D.MaxResources, GenericGpuPreparation2D.MaxResources + 1);
            GenericArguments2D args = GenericGpuPreparation2D.Arguments(scene.Extent, table, scene.GetBuffer<Vector4>(0));
            args.Paint = table.WriteBuffer(2, destination, ShaderBufferAccess.ReadWrite);
            GenericGpuPreparation2D.ResourceUpload resources = await GenericGpuPreparation2D.ResourcesAsync(gpu, views, table, artifact!, cancellationToken);
            args.Resources = resources.Reference;
            GenericGpuPreparation2D.Compute(gpu, artifact!, table, args, count, resources.Encode, record => gpu.Encode(output, stageRecord =>
            {
                record(stageRecord);
                stageRecord.Commands.Barrier(new BufferBarrierDesc<Vector4>
                {
                    Buffer = destination,
                    Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite),
                    After = new(PipelineStage.FragmentShader | PipelineStage.Copy, ResourceAccess.ShaderRead | ResourceAccess.CopyRead),
                });
            }));
            return output;
        }

        public void Dispose()
        {
        }
    }
}
