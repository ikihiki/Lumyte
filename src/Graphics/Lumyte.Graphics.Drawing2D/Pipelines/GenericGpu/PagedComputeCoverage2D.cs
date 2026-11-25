using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Evaluates canonical sixteen-sample coverage into independent native storage.</summary>
public sealed class PagedComputeCoverage2D : IStage<Device<SceneV1>, Device<FrameV1>, Device<Coverage16V1>>
{
    internal PagedComputeCoverage2D(SamplePolicy2D samples, GenericPagingLimits2D limits)
    {
        ArgumentNullException.ThrowIfNull(samples);
        samples.Validate();
        ArgumentNullException.ThrowIfNull(limits);
        Factory = new Implementation(null, limits);
    }

    /// <summary>Gets the executable factory.</summary>
    public IStageFactory<Device<SceneV1>, Device<FrameV1>, Device<Coverage16V1>> Factory { get; }

    private sealed class Implementation(StageArtifactLease? artifact, GenericPagingLimits2D limits) : IStageFactory<Device<SceneV1>, Device<FrameV1>, Device<Coverage16V1>>, IStageProcessor<Device<SceneV1>, Device<FrameV1>, Device<Coverage16V1>>
    {
        public StageRequirements Describe(StageInputDescriptions inputs) => PagedGpuPreparation2D.Describe<PagedComputeCoverage2D>(inputs, GraphicsExecutionDomain.Compute, 4, resources: false, limits: limits);

        public IStageProcessor<Device<SceneV1>, Device<FrameV1>, Device<Coverage16V1>> Create(StageBuildContext context) => new Implementation(GenericGpuPreparation2D.Artifact<PagedComputeCoverage2D>(context), limits);

        public async ValueTask<ValueLease<Device<Coverage16V1>>> PrepareAsync(StagePreparationContext context, ValueLease<Device<SceneV1>> a, ValueLease<Device<FrameV1>> b, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphicsStagePreparationContext gpu = GenericGpuPreparation2D.Preparation(context);
            DeviceBindings<SceneV1> scene = gpu.GetDeviceBindings(a);
            DeviceBindings<FrameV1> frame = gpu.GetDeviceBindings(b);
            GenericGpuPreparation2D.Match(scene.Extent, frame.Extent);
            long count = GenericGpuPreparation2D.Count(scene.Extent);
            BufferSlice<Vector4> sceneRange = scene.GetBuffer<Vector4>(0);
            PagedGpuPreparation2D.ValidateSource(gpu, sceneRange);
            PagedGpuPreparation2D.Plan plan = PagedGpuPreparation2D.Admit(gpu, count, sceneRange, limits);
            PagedGpuPreparation2D.Reserve(gpu, count, 4, plan);
            ValueLease<Device<Coverage16V1>> output = gpu.AllocateDeviceOutput<Coverage16V1>(new(count, scene.Extent.Metadata));
            BufferSlice<float> destination = gpu.GetDeviceBindings(output).GetBuffer<float>(0);
            var records = new List<Action<StageRecordContext>>();
            foreach (PagedGpuPreparation2D.SampleWindow window in plan.Windows)
            {
                IArgumentTable table = PagedGpuPreparation2D.Table(gpu);
                PagedGpuPreparation2D.SceneUpload sceneUpload = await PagedGpuPreparation2D.SceneAsync(gpu, table, artifact!, sceneRange, plan, cancellationToken);
                GenericArguments2D args = PagedGpuPreparation2D.Arguments(scene.Extent, sceneUpload.Reference, plan.PageCells, plan.SceneCells);
                args.Coverage = table.WriteBuffer(4, PagedGpuPreparation2D.Window(destination, window), ShaderBufferAccess.ReadWrite);
                args.SampleOffset = window.Offset;
                args.SampleCount = window.Count;
                PagedGpuPreparation2D.Compute(gpu, artifact!, table, args, window.Count, sceneUpload.Encode, record => records.Add(stageRecord =>
                {
                    record(stageRecord);
                    stageRecord.Commands.Barrier(new BufferBarrierDesc<float>
                    {
                        Buffer = PagedGpuPreparation2D.Window(destination, window),
                        Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite),
                        After = new(PipelineStage.FragmentShader | PipelineStage.Copy, ResourceAccess.ShaderRead | ResourceAccess.CopyRead),
                    });
                }));
            }

            gpu.Encode(output, record =>
            {
                foreach (Action<StageRecordContext> encode in records)
                {
                    encode(record);
                }
            });
            return output;
        }

        public void Dispose()
        {
        }
    }
}
