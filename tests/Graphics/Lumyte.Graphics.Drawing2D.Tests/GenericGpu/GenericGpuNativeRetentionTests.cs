using System.Numerics;
using Lumyte.Execution;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Pipelines;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Lumyte.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks native completion-published generic immutable ownership.</summary>
public sealed partial class GenericGpuNativeTests
{
    /// <summary>Retains real immutable upload chunks across three overlapping generic frames, placement and COW edits.</summary>
    /// <param name="viaGraph">Whether each frame is borrowed into the existing parent Graph.</param>
    [GenericGpuNativeTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Execution", "Gpu")]
    public void GenericRetainedUploadsPublishOnlyAfterCompletionAndPreserveThreeVersions(bool viaGraph)
        => RetainedUploadsPublishOnlyAfterCompletionAndPreserveThreeVersions(viaGraph, false);

    /// <summary>Preserves immutable input identities and source leases on the concrete native mesh endpoint.</summary>
    [GenericGpuNativeMeshFact]
    [Trait("Execution", "Gpu")]
    public void MeshRetainedUploadsPreserveThreeVersionsDirectAndGraph()
    {
        RetainedUploadsPublishOnlyAfterCompletionAndPreserveThreeVersions(false, true);
        RetainedUploadsPublishOnlyAfterCompletionAndPreserveThreeVersions(true, true);
    }

    private static void RetainedUploadsPublishOnlyAfterCompletionAndPreserveThreeVersions(bool viaGraph, bool mesh)
    {
        IGraphicDevice device = CreateDevice();
        using var deviceOwner = (IDisposable)device;
        var runtime = new ExecutionRuntime([]);
        runtime.PrepareOnCurrentHost(OwnerPlacement.Host, CpuExecutionMode.Cooperative, 1, new ExecutionOptions());
        Pump(runtime, runtime.StartAsync().AsTask());
        try
        {
            var graph = new PipelineGraph<RenderRequest2D>();
            Port<Host<CommandsV1>> commands = graph.Stage(RenderFields2D.Commands(), graph.Input);
            Port<Host<ResourceManifestV1>> manifest = graph.Stage(RenderFields2D.ResourceManifest(), graph.Input);
            Port<Host<FrameV1>> hostFrame = graph.Stage(RenderFields2D.Frame(), graph.Input);
            Port<Device<SceneV1>> scenePort = graph.Stage(CpuStages2D.Normalize(new(16, 64, 64, 64)), commands, manifest, hostFrame).Then(Transfers.Upload<SceneV1>());
            Port<Device<FrameV1>> framePort = graph.Stage(RenderFields2D.Frame(), graph.Input).Then(Transfers.Upload<FrameV1>());
            Port<Device<ResourceViewsV1>> views = graph.Stage(RenderFields2D.ResourceViews(), graph.Input);
            Port<Device<TargetV1>> target = graph.Stage(RenderFields2D.Target(), graph.Input);
            PipelineDefinition<RenderRequest2D, Device<TargetWritesV1>> definition = graph.Complete(mesh
                ? graph.Stage(MeshStages2D.EvaluateAndComposite(new(16, 1)), scenePort, views, framePort, target)
                : graph.Stage(RasterStages2D.EvaluateAndComposite(new(16, 1)), scenePort, views, framePort, target));
            var policy = new GraphicsPipelinePolicy(64 * 1024 * 1024, 16 * 1024 * 1024, 16 * 1024 * 1024, 3)
            {
                ImmutableUploads = new(1024 * 1024, 128, 256),
            };
            using var build = new GraphicsPipelineBuildHost(runtime, device, Artifacts(), policy);
            using IPipeline<RenderRequest2D, Device<TargetWritesV1>> pipeline = build.Build(definition);
            using var renderer = new Renderer2D(device, pipeline, new(true, true, true, true, true, true, true, true, true, true, true), new(policy, DrawingArgumentTableMode.Auto, new() { EnableResidentInstances = true, MaxCachedBytes = 1024 * 1024 }));
            ImmutableUploadCache cache = ((IGraphicsPipelineInfo)pipeline).ImmutableUploads!;
            using var scene = new DrawingScene2D();
            DrawingNodeId2D node = scene.Insert(scene.Root, 0, RetentionContent(false));
            using DrawingSnapshot2D original = scene.CaptureSnapshot();
            using var warm = new RetentionSlot(device, renderer, original, runtime, 0, viaGraph);
            Assert.All(cache.Snapshot().Completed, completed => Assert.False(completed));
            warm.Submit(device);
            Assert.Throws<InvalidOperationException>(() => warm.Frame.Retire());
            Assert.All(cache.Snapshot().Completed, completed => Assert.False(completed));
            warm.Complete(runtime);
            (long Created, long Hits, long Bytes, object[] Owners, bool[] Completed) initialized = cache.Snapshot();
            Assert.NotEmpty(initialized.Owners);
            Assert.All(initialized.Completed, completed => Assert.True(completed));
            Assert.True(initialized.Created >= 3);

            using var old = new RetentionSlot(device, renderer, original, runtime, 0, viaGraph);
            scene.SetTransform(node, Matrix3x2.CreateTranslation(3, 1));
            using DrawingSnapshot2D moved = scene.CaptureSnapshot();
            using var next = new RetentionSlot(device, renderer, moved, runtime, 1, viaGraph);
            Assert.Equal(initialized.Created, cache.Snapshot().Created);
            Assert.True(cache.Snapshot().Hits >= initialized.Owners.Length * 2);
            scene.ReplaceContent(node, RetentionContent(true));
            using DrawingSnapshot2D changed = scene.CaptureSnapshot();
            using var replacement = new RetentionSlot(device, renderer, changed, runtime, 2, viaGraph);
            (long Created, long Hits, long Bytes, object[] Owners, bool[] Completed) edited = cache.Snapshot();
            Assert.True(edited.Created > initialized.Created);
            Assert.True(edited.Created - initialized.Created < initialized.Created);
            Assert.All(initialized.Owners, owner => Assert.Contains(owner, edited.Owners));
            original.Dispose();
            moved.Dispose();
            changed.Dispose();
            scene.Dispose();
            old.Submit(device);
            next.Submit(device);
            replacement.Submit(device);
            Assert.Equal(3, renderer.RetentionStatistics.ActiveFrames);
            Assert.Contains(warm.Copies, copy => initialized.Owners.Contains(copy.Destination));
            Assert.DoesNotContain(old.Copies, copy => initialized.Owners.Contains(copy.Destination));
            Assert.DoesNotContain(next.Copies, copy => initialized.Owners.Contains(copy.Destination));
            Assert.True(replacement.Copies.Count(copy => edited.Owners.Contains(copy.Destination)) < warm.Copies.Count(copy => initialized.Owners.Contains(copy.Destination)));
            replacement.Complete(runtime);
            old.Complete(runtime);
            next.Complete(runtime);
            Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
            Assert.All(cache.Snapshot().Completed, completed => Assert.True(completed));
        }
        finally
        {
            Pump(runtime, runtime.StopAsync().AsTask());
        }
    }

    private static DrawList2D RetentionContent(bool green)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(2, 2, 4, 4), new SRgba<float>(green ? 0 : 1, green ? 1 : 0, 0, 1));
        return canvas.End();
    }

    private sealed class RetentionSlot : IDisposable
    {
        private readonly IGraphicsTexture _texture;
        private readonly IGraphicsTextureView _view;
        private readonly IGraphicsBuffer<byte> _readback;
        private readonly IGraphicsCommandBuffer _commands;
        private readonly uint _pitch;
        private readonly int _version;
        private readonly bool _viaGraph;
        private IGraphicsSubmission? _submission;
        private bool _retired;
        private CompiledRenderGraph? _graphPlan;

        internal RetentionSlot(IGraphicDevice device, Renderer2D renderer, DrawingSnapshot2D source, ExecutionRuntime runtime, int version, bool viaGraph)
        {
            _version = version;
            _viaGraph = viaGraph;
            _texture = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            _view = _texture.CreateView();
            uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb).BytesPerRowAlignment;
            _pitch = ((64 + alignment - 1) / alignment) * alignment;
            _readback = device.CreateBuffer<byte>(new() { Count = _pitch * 16, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            _commands = device.CreateCommandBuffer(new());
            Task<PreparedDrawingFrame2D> preparation = renderer.PrepareAsync(source, new() { View = _view, LoadOp = AttachmentLoadOp.Clear }).AsTask();
            Pump(runtime, preparation);
            Frame = preparation.Result;
        }

        internal PreparedDrawingFrame2D Frame { get; }

        internal List<(object Destination, ulong Bytes)> Copies { get; } = [];

        public void Dispose()
        {
            if (!_retired)
            {
                if (_submission is not null)
                {
                    throw new InvalidOperationException("Retain submitted test resources until completion.");
                }

                _commands.Dispose();
                Frame.Dispose();
            }

            _submission?.Dispose();
            _commands.Dispose();
            _readback.Dispose();
            _view.Dispose();
            _texture.Dispose();
        }

        internal void Submit(IGraphicDevice device)
        {
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            _commands.Barrier(new TextureBarrierDesc { Texture = _texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
            var observed = new UploadObservationCommands(_commands, Copies);
            if (_viaGraph)
            {
                using var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(device);
                RenderGraphTexture target = graph.ImportTexture(_texture, TextureState.ColorAttachment, color);
                var bindings = new DrawingGraphBindings2D(graph);
                bindings.BindTexture(_texture, target);
                graph.AppendResident2D(Frame, new() { Texture = target, Description = new() { Width = 16, Height = 16, Format = _texture.Format, Usage = _texture.Usage }, PixelScale = 1 }, bindings);
                graph.MarkOutput(target);
                _graphPlan = graph.Compile();
                _graphPlan.Record(observed);
            }
            else
            {
                Frame.Record(observed);
            }

            _commands.Barrier(new TextureBarrierDesc { Texture = _texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            _commands.CopyTextureToBuffer(new() { Texture = _texture, Width = 16, Height = 16 }, new() { Buffer = _readback.SliceElements(0, _readback.Count), BytesPerRow = _pitch, RowsPerImage = 16 });
            _commands.Barrier(new BufferBarrierDesc<byte> { Buffer = _readback.SliceElements(0, _readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            _commands.Finish();
            _submission = device.Queue.Submit([_commands]);
            Frame.AttachSubmission(_submission);
        }

        internal void Complete(ExecutionRuntime runtime)
        {
            Pump(runtime, _submission!.WaitAsync().AsTask());
            _commands.Dispose();
            _graphPlan?.Dispose();
            Frame.Retire();
            _retired = true;
            Pump(runtime, _readback.MapAsync().AsTask());
            byte[] bytes = new byte[_readback.Count];
            _readback.CopyTo(bytes);
            _readback.Unmap();
            for (uint y = 0; y < 16; y++)
            {
                for (uint x = 0; x < 16; x++)
                {
                    uint left = _version == 0 ? 2u : 5u;
                    uint top = _version == 0 ? 2u : 3u;
                    bool inside = x >= left && x < left + 4 && y >= top && y < top + 4;
                    int at = checked((int)((y * _pitch) + (x * 4)));
                    Assert.Equal(inside && _version != 2 ? 255 : 0, bytes[at]);
                    Assert.Equal(inside && _version == 2 ? 255 : 0, bytes[at + 1]);
                    Assert.Equal(0, bytes[at + 2]);
                    Assert.Equal(inside ? 255 : 0, bytes[at + 3]);
                }
            }
        }
    }
}
