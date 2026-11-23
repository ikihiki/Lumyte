using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Evaluates complete canonical programs with ordered native mesh draws.</summary>
public sealed class MeshEvaluateAndComposite2D : IStage<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<TargetV1>, Device<TargetWritesV1>>
{
    internal MeshEvaluateAndComposite2D(SamplePolicy2D samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        samples.Validate();
        Factory = new Implementation(null);
    }

    /// <summary>Gets the executable factory.</summary>
    public IStageFactory<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<TargetV1>, Device<TargetWritesV1>> Factory { get; }

    private sealed class Implementation(StageArtifactLease? artifact) : IStageFactory<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<TargetV1>, Device<TargetWritesV1>>, IStageProcessor<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<TargetV1>, Device<TargetWritesV1>>
    {
        public StageRequirements Describe(StageInputDescriptions inputs) => GenericGpuPreparation2D.Describe<MeshEvaluateAndComposite2D>(inputs, GraphicsExecutionDomain.Mesh, 0, 3, resources: true);

        public IStageProcessor<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<TargetV1>, Device<TargetWritesV1>> Create(StageBuildContext context) => new Implementation(GenericGpuPreparation2D.Artifact<MeshEvaluateAndComposite2D>(context));

        public async ValueTask<ValueLease<Device<TargetWritesV1>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<SceneV1>> a, ValueLease<Device<ResourceViewsV1>> b, ValueLease<Device<FrameV1>> c, ValueLease<Device<TargetV1>> d, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphicsStagePreparationContext gpu = GenericGpuPreparation2D.Preparation(context);
            DeviceBindings<SceneV1> scene = gpu.GetDeviceBindings(a);
            DeviceBindings<TargetV1> target = gpu.GetDeviceBindings(d);
            GenericGpuPreparation2D.Match(scene.Extent, target.Extent);
            DeviceBindings<ResourceViewsV1> views = gpu.GetDeviceBindings(b);
            DeviceBindings<FrameV1> frame = gpu.GetDeviceBindings(c);
            GenericGpuPreparation2D.Match(scene.Extent, views.Extent, frame.Extent);
            ValueLease<Device<TargetWritesV1>> output = gpu.AllocateDeviceOutput<TargetWritesV1>(new(1, target.Extent.Metadata));
            IArgumentTable table = GenericGpuPreparation2D.Table(gpu, GenericGpuPreparation2D.MaxResources, GenericGpuPreparation2D.MaxResources + 1);
            GenericArguments2D args = GenericGpuPreparation2D.Arguments(scene.Extent, table, scene.GetBuffer<Vector4>(0));
            GenericGpuPreparation2D.ResourceUpload resources = await GenericGpuPreparation2D.ResourcesAsync(gpu, views, table, artifact!, cancellationToken);
            args.Resources = resources.Reference;
            GenericGpuPreparation2D.Raster(gpu, artifact!, table, args, target.GetTexture(0), resources.Encode, record => gpu.Encode(output, record));
            return output;
        }

        public void Dispose()
        {
        }
    }
}
