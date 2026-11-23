using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Pipelines;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Evaluates canonical sixteen-sample paint into independent native storage.</summary>
public sealed class PagedComputePaint2D : IStage<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>>
{
    internal PagedComputePaint2D(SamplePolicy2D samples, GenericPagingLimits2D limits)
    {
        ArgumentNullException.ThrowIfNull(samples);
        samples.Validate();
        ArgumentNullException.ThrowIfNull(limits);
        Factory = new Implementation(null, limits);
    }

    /// <summary>Gets the executable factory.</summary>
    public IStageFactory<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>> Factory { get; }

    private sealed class Implementation(StageArtifactLease? artifact, GenericPagingLimits2D limits) : IStageFactory<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>>, IStageProcessor<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>>
    {
        public StageRequirements Describe(StageInputDescriptions inputs) => PagedGpuPreparation2D.Describe<PagedComputePaint2D>(inputs, GraphicsExecutionDomain.Compute, 16, resources: true, limits: limits);

        public IStageProcessor<Device<SceneV1>, Device<ResourceViewsV1>, Device<FrameV1>, Device<Paint16V1>> Create(StageBuildContext context) => new Implementation(GenericGpuPreparation2D.Artifact<PagedComputePaint2D>(context), limits);

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
            PagedGpuPreparation2D.ValidateResources(gpu, views);
            BufferSlice<Vector4> sceneRange = scene.GetBuffer<Vector4>(0);
            PagedGpuPreparation2D.ValidateSource(gpu, sceneRange);
            PagedGpuPreparation2D.Plan plan = PagedGpuPreparation2D.Admit(gpu, count, sceneRange, limits, views);
            PagedGpuPreparation2D.Reserve(gpu, count, 16, plan, views);
            ValueLease<Device<Paint16V1>> output = gpu.AllocateDeviceOutput<Paint16V1>(new(count, scene.Extent.Metadata));
            BufferSlice<Vector4> destination = gpu.GetDeviceBindings(output).GetBuffer<Vector4>(0);
            var records = new List<Action<StageRecordContext>>();
            foreach (PagedGpuPreparation2D.SampleWindow window in plan.Windows)
            {
                IArgumentTable table = PagedGpuPreparation2D.Table(gpu, Math.Max(1, Convert.ToInt32(views.Extent.Metadata["viewCount"])), Convert.ToInt32(views.Extent.Metadata["samplerCount"]) + 1);
                PagedGpuPreparation2D.SceneUpload sceneUpload = await PagedGpuPreparation2D.SceneAsync(gpu, table, artifact!, sceneRange, plan, cancellationToken);
                GenericArguments2D args = PagedGpuPreparation2D.Arguments(scene.Extent, sceneUpload.Reference, plan.PageCells, plan.SceneCells);
                args.Paint = table.WriteBuffer(4, PagedGpuPreparation2D.Window(destination, window), ShaderBufferAccess.ReadWrite);
                PagedGpuPreparation2D.ResourceUpload resources = await PagedGpuPreparation2D.ResourcesAsync(gpu, views, table, artifact!, cancellationToken);
                args.Resources = resources.Reference;
                args.SampleOffset = window.Offset;
                args.SampleCount = window.Count;
                Action<IGraphicsCommandBuffer> before = commands =>
                {
                    sceneUpload.Encode(commands);
                    resources.Encode(commands);
                };
                PagedGpuPreparation2D.Compute(gpu, artifact!, table, args, window.Count, before, record => records.Add(stageRecord =>
                {
                    record(stageRecord);
                    stageRecord.Commands.Barrier(new BufferBarrierDesc<Vector4>
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
