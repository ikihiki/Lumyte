using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Composes independent correlated samples with ordered native raster draws.</summary>
public sealed class PagedRasterCompose2D : IStage<Device<SceneV1>, Device<Coverage16V1>, Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>>
{
    internal PagedRasterCompose2D(SamplePolicy2D samples, GenericPagingLimits2D limits)
    {
        ArgumentNullException.ThrowIfNull(samples);
        samples.Validate();
        ArgumentNullException.ThrowIfNull(limits);
        Factory = new Implementation(null, limits);
    }

    /// <summary>Gets the executable factory.</summary>
    public IStageFactory<Device<SceneV1>, Device<Coverage16V1>, Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>> Factory { get; }

    private sealed class Implementation(StageArtifactLease? artifact, GenericPagingLimits2D limits) : IStageFactory<Device<SceneV1>, Device<Coverage16V1>, Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>>, IStageProcessor<Device<SceneV1>, Device<Coverage16V1>, Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>>
    {
        public StageRequirements Describe(StageInputDescriptions inputs) => PagedGpuPreparation2D.Describe<PagedRasterCompose2D>(inputs, GraphicsExecutionDomain.Raster, 0, 4, resources: false, limits: limits);

        public IStageProcessor<Device<SceneV1>, Device<Coverage16V1>, Device<Paint16V1>, Device<Clip16V1>, Device<TargetV1>, Device<TargetWritesV1>> Create(StageBuildContext context) => new Implementation(GenericGpuPreparation2D.Artifact<PagedRasterCompose2D>(context), limits);

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

            BufferSlice<Vector4> sceneRange = scene.GetBuffer<Vector4>(0);
            PagedGpuPreparation2D.ValidateSource(gpu, sceneRange);
            BufferSlice<float> coverageRange = coverage.GetBuffer<float>(0);
            PagedGpuPreparation2D.ValidateSource(gpu, coverageRange);
            BufferSlice<Vector4> paintRange = paint.GetBuffer<Vector4>(0);
            PagedGpuPreparation2D.ValidateSource(gpu, paintRange);
            BufferSlice<float> clipRange = clip.GetBuffer<float>(0);
            PagedGpuPreparation2D.ValidateSource(gpu, clipRange);
            PagedGpuPreparation2D.Plan plan = PagedGpuPreparation2D.Admit(gpu, count, sceneRange, limits);
            ValueLease<Device<TargetWritesV1>> output = gpu.AllocateDeviceOutput<TargetWritesV1>(new(1, target.Extent.Metadata));
            var bindings = new List<(IArgumentTable Table, GenericArguments2D Arguments)>();
            foreach (PagedGpuPreparation2D.SampleWindow window in plan.Windows)
            {
                IArgumentTable table = PagedGpuPreparation2D.Table(gpu);
                GenericArguments2D args = PagedGpuPreparation2D.Arguments(scene.Extent, null, 0);
                args.Coverage = table.WriteBuffer(1, PagedGpuPreparation2D.Window(coverageRange, window));
                args.Paint = table.WriteBuffer(2, PagedGpuPreparation2D.Window(paintRange, window));
                args.Clip = table.WriteBuffer(3, PagedGpuPreparation2D.Window(clipRange, window));
                args.SampleOffset = window.Offset;
                args.SampleCount = window.Count;
                bindings.Add((table, args));
            }

            PagedGpuPreparation2D.Raster(gpu, artifact!, bindings, target.GetTexture(0), record => gpu.Encode(output, record));
            return ValueTask.FromResult(output);
        }

        public void Dispose()
        {
        }
    }
}
