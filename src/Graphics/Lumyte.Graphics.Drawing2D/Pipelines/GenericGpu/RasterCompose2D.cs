using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Composes independent correlated samples with ordered native raster draws.</summary>
public sealed class RasterCompose2D : IStage<Device<SceneV1>, Device<Coverage16V1>, Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>>
{
    internal RasterCompose2D(SamplePolicy2D samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        samples.Validate();
        Factory = new Implementation(null);
    }

    /// <summary>Gets the executable factory.</summary>
    public IStageFactory<Device<SceneV1>, Device<Coverage16V1>, Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>> Factory { get; }

    private sealed class Implementation(StageArtifactLease? artifact) : IStageFactory<Device<SceneV1>, Device<Coverage16V1>, Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>>, IStageProcessor<Device<SceneV1>, Device<Coverage16V1>, Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>>
    {
        public StageRequirements Describe(StageInputDescriptions inputs) => GenericGpuPreparation2D.Describe<RasterCompose2D>(inputs, GraphicsExecutionDomain.Raster, 0, 4, resources: false);

        public IStageProcessor<Device<SceneV1>, Device<Coverage16V1>, Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>> Create(StageBuildContext context) => new Implementation(GenericGpuPreparation2D.Artifact<RasterCompose2D>(context));

        public ValueTask<ValueLease<Device<TargetWritesV1>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<SceneV1>> a, ValueLease<Device<Coverage16V1>> b, ValueLease<Device<Paint16V1>> c, ValueLease<Device<Clip16V1>> d, ValueLease<Device<TargetV1>> e, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphicsStagePreparationContext gpu = GenericGpuPreparation2D.Preparation(context);
            DeviceBindings<SceneV1> scene = gpu.GetDeviceBindings(a);
            DeviceBindings<TargetV1> target = gpu.GetDeviceBindings(e);
            GenericGpuPreparation2D.Match(scene.Extent, target.Extent);
            DeviceBindings<Coverage16V1> coverage = gpu.GetDeviceBindings(b);
            DeviceBindings<Paint16V1> paint = gpu.GetDeviceBindings(c);
            DeviceBindings<Clip16V1> clip = gpu.GetDeviceBindings(d);
            GenericGpuPreparation2D.Match(scene.Extent, coverage.Extent, paint.Extent, clip.Extent);
            long count = GenericGpuPreparation2D.Count(scene.Extent);
            if (coverage.Extent.ElementCount != count || paint.Extent.ElementCount != count || clip.Extent.ElementCount != count)
            {
                throw new InvalidOperationException("Canonical intermediate sample count mismatch.");
            }

            ValueLease<Device<TargetWritesV1>> output = gpu.AllocateDeviceOutput<TargetWritesV1>(new(1, target.Extent.Metadata));
            IArgumentTable table = GenericGpuPreparation2D.Table(gpu);
            GenericArguments2D args = GenericGpuPreparation2D.Arguments(scene.Extent, table, scene.GetBuffer<Vector4>(0));
            args.Coverage = table.WriteBuffer(1, coverage.GetBuffer<float>(0));
            args.Paint = table.WriteBuffer(2, paint.GetBuffer<Vector4>(0));
            args.Clip = table.WriteBuffer(3, clip.GetBuffer<float>(0));
            GenericGpuPreparation2D.Raster(gpu, artifact!, table, args, target.GetTexture(0), null, record => gpu.Encode(output, record));
            return ValueTask.FromResult(output);
        }

        public void Dispose()
        {
        }
    }
}
