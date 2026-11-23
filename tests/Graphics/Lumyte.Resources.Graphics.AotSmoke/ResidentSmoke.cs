using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal static partial class ResidentSmoke
{
    internal static async Task RunAsync()
    {
        using var device = WgpuDevice.Create();
        using IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using RendererBuildHost2D host = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionGpuLimits(maxFramesInFlight: 3, maxCachedAssets: 8, maxCachedBytes: 1024 * 1024, maxWorkingBytes: 16 * 1024 * 1024))[
            Renderer.FillRect()[StandardFillRect()],
            Renderer.FillRoundedRect()[StandardFillRoundedRect()],
            Renderer.FillCircle()[StandardFillCircle()],
            Renderer.DrawLine()[StandardDrawLine()],
            Renderer.FillPath()[StandardFillPath()],
            Renderer.StrokePath()[StandardStrokePath()],
            Renderer.DrawImage()[StandardDrawImage()],
            Renderer.DrawCoverageMask()[StandardDrawCoverageMask()],
            Renderer.DrawDistanceField()[StandardDrawDistanceField()],
            Renderer.DrawMesh()[StandardDrawMesh()],
            Renderer.DrawPaintGraph()[StandardDrawPaintGraph()]].CreateHost();
        var resources = new DrawingResources2D(device, pipeline);
        using IRenderer2D renderer = host.BuildRenderer(device, resources, null, null);
        using var scene = new DrawingScene2D();
        DrawingNodeId2D node = scene.Insert(scene.Root, 0, Content(false));
        using DrawingSnapshot2D original = scene.CaptureSnapshot();
        Slot warm = await Slot.PrepareAsync(device, renderer, original, 0);
        warm.Submit(device);
        await warm.CompleteAsync();
        Slot old = await Slot.PrepareAsync(device, renderer, original, 0);
        scene.SetTransform(node, Matrix3x2.CreateTranslation(3, 1));
        using DrawingSnapshot2D moved = scene.CaptureSnapshot();
        Slot next = await Slot.PrepareAsync(device, renderer, moved, 1);
        scene.ReplaceContent(node, Content(true));
        using DrawingSnapshot2D changed = scene.CaptureSnapshot();
        Slot replacement = await Slot.PrepareAsync(device, renderer, changed, 2);
        old.Submit(device);
        next.Submit(device);
        replacement.Submit(device);
        if (renderer.RetentionStatistics.ActiveFrames != 3)
        {
            throw new InvalidOperationException("Three independent submitted owners are required.");
        }

        await replacement.CompleteAsync();
        await old.CompleteAsync();
        await next.CompleteAsync();
        Console.WriteLine("Native AOT retained factory: three submissions, out-of-order retirement and exact property/COW pixels passed.");
    }

    private static DrawList2D Content(bool green)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(2, 2, 4, 4), new SRgba<float>(green ? 0 : 1, green ? 1 : 0, 0, 1));
        return canvas.End();
    }

    private sealed class Slot
    {
        private readonly IGraphicsTexture _texture;
        private readonly IGraphicsTextureView _view;
        private readonly IGraphicsBuffer<byte> _readback;
        private readonly IGraphicsCommandBuffer _commands;
        private readonly uint _pitch;
        private readonly int _version;
        private PreparedDrawingFrame2D _frame = null!;
        private IGraphicsSubmission? _submission;

        private Slot(IGraphicDevice device, int version)
        {
            _version = version;
            _texture = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            _view = _texture.CreateView();
            uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb).BytesPerRowAlignment;
            _pitch = ((64 + alignment - 1) / alignment) * alignment;
            _readback = device.CreateBuffer<byte>(new() { Count = _pitch * 16, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
            _commands = device.CreateCommandBuffer(new());
        }

        internal static async Task<Slot> PrepareAsync(IGraphicDevice device, IRenderer2D renderer, DrawingSnapshot2D snapshot, int version)
        {
            var slot = new Slot(device, version);
            slot._frame = await renderer.PrepareAsync(snapshot, new() { View = slot._view, LoadOp = AttachmentLoadOp.Clear });
            return slot;
        }

        internal void Submit(IGraphicDevice device)
        {
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
            _commands.Barrier(new TextureBarrierDesc { Texture = _texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
            _frame.Record(_commands);
            _commands.Barrier(new TextureBarrierDesc { Texture = _texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copy });
            _commands.CopyTextureToBuffer(new() { Texture = _texture, Width = 16, Height = 16 }, new() { Buffer = _readback.SliceElements(0, _readback.Count), BytesPerRow = _pitch, RowsPerImage = 16 });
            _commands.Barrier(new BufferBarrierDesc<byte> { Buffer = _readback.SliceElements(0, _readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            _commands.Finish();
            _submission = device.Queue.Submit([_commands]);
            _frame.AttachSubmission(_submission);
        }

        internal async Task CompleteAsync()
        {
            await _submission!.WaitAsync();
            _commands.Dispose();
            _frame.Retire();
            await _readback.MapAsync();
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
                    int offset = checked((int)((y * _pitch) + (x * 4)));
                    if (bytes[offset] != (inside && _version != 2 ? 255 : 0) || bytes[offset + 1] != (inside && _version == 2 ? 255 : 0) || bytes[offset + 2] != 0 || bytes[offset + 3] != (inside ? 255 : 0))
                    {
                        throw new InvalidOperationException("Retained root property or immutable version pixel mismatch.");
                    }
                }
            }

            _frame.Dispose();
            _submission.Dispose();
            _readback.Dispose();
            _view.Dispose();
            _texture.Dispose();
        }
    }
}
