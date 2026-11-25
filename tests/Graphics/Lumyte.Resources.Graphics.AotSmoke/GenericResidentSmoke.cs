using System.Numerics;
using Lumyte.Execution;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Pipelines;
using Lumyte.Graphics.Wgpu;
using Lumyte.Pipelines;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal static partial class ResidentSmoke
{
    internal static void RunGeneric()
    {
        using var device = WgpuDevice.Create();
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new());
        Complete(runtime.StartAsync().AsTask());
        try
        {
            var graph = new PipelineGraph<RenderRequest2D>();
            Port<Host<CommandsV1>> commands = graph.Stage(RenderFields2D.Commands(), graph.Input);
            Port<Host<ResourceManifestV1>> manifest = graph.Stage(RenderFields2D.ResourceManifest(), graph.Input);
            Port<Host<FrameV1>> frame = graph.Stage(RenderFields2D.Frame(), graph.Input);
            Port<Device<SceneV1>> scenePort = graph.Stage(CpuStages2D.Normalize(new(16, 64, 64, 64)), commands, manifest, frame).Then(Transfers.Upload<SceneV1>());
            Port<Device<FrameV1>> deviceFrame = frame.Then(Transfers.Upload<FrameV1>());
            Port<Device<ResourceViewsV1>> views = graph.Stage(RenderFields2D.ResourceViews(), graph.Input);
            Port<Device<TargetV1>> target = graph.Stage(RenderFields2D.Target(), graph.Input);
            var policy = new GraphicsPipelinePolicy(64 * 1024 * 1024, 16 * 1024 * 1024, 16 * 1024 * 1024, 3)
            {
                ImmutableUploads = new(1024 * 1024, 128, 256),
            };
            using var build = new GraphicsPipelineBuildHost(runtime, device, GenericGpuArtifactProvider2D.CreateEmbedded(), policy);
            using IPipeline<RenderRequest2D, Device<TargetWritesV1>> pipeline = build.Build(graph.Complete(graph.Stage(RasterStages2D.EvaluateAndComposite(new(16, 1)), scenePort, views, deviceFrame, target)));
            using var renderer = new Renderer2D(device, pipeline, new(true, true, true, true, true, true, true, true, true, true, true), new(policy, DrawingArgumentTableMode.Auto, new() { EnableResidentInstances = true, MaxCachedBytes = 1024 * 1024 }));
            using var scene = new DrawingScene2D();
            DrawingNodeId2D node = scene.Insert(scene.Root, 0, Content(false));
            using DrawingSnapshot2D original = scene.CaptureSnapshot();
            Task<Slot> warming = Slot.PrepareAsync(device, renderer, original, 0);
            Complete(warming);
            Slot warm = warming.Result;
            warm.Submit(device);
            Complete(warm.CompleteAsync());
            Task<Slot> preparing = Slot.PrepareAsync(device, renderer, original, 0);
            Complete(preparing);
            Slot old = preparing.Result;
            scene.SetTransform(node, Matrix3x2.CreateTranslation(3, 1));
            using DrawingSnapshot2D moved = scene.CaptureSnapshot();
            preparing = Slot.PrepareAsync(device, renderer, moved, 1);
            Complete(preparing);
            Slot next = preparing.Result;
            scene.ReplaceContent(node, Content(true));
            using DrawingSnapshot2D changed = scene.CaptureSnapshot();
            preparing = Slot.PrepareAsync(device, renderer, changed, 2);
            Complete(preparing);
            Slot replacement = preparing.Result;
            original.Dispose();
            moved.Dispose();
            changed.Dispose();
            scene.Dispose();
            old.Submit(device);
            next.Submit(device);
            replacement.Submit(device);
            if (renderer.RetentionStatistics.ActiveFrames != 3)
            {
                throw new InvalidOperationException("Generic retained frames must own three independent submissions.");
            }

            Complete(replacement.CompleteAsync());
            Complete(old.CompleteAsync());
            Complete(next.CompleteAsync());
            Console.WriteLine("Native AOT generic IPipeline: public Normalize/Upload, three submissions, disposed sources, reverse retirement and exact property/COW pixels passed.");
        }
        finally
        {
            Complete(runtime.StopAsync().AsTask());
        }

        void Complete(Task task)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                runtime.PumpHostLifecycle(64, TimeSpan.FromMilliseconds(2));
                runtime.PumpJobs(64, TimeSpan.FromMilliseconds(2));
                if (deadline.Elapsed > TimeSpan.FromSeconds(60))
                {
                    throw new TimeoutException("Generic AOT smoke exceeded its cooperative execution deadline.");
                }

                Thread.Yield();
            }

            task.GetAwaiter().GetResult();
        }
    }
}
