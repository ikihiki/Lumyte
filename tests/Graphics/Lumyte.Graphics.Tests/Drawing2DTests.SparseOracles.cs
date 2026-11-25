using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Tests;

/// <summary>Independent analytic oracles for the two alternative GPU execution strategies.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Executes curves, holes and strokes over a separately submitted Vulkan background.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task VulkanAlternativeRendererGeometryLoadOraclesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckAlternativeGeometryLoadOraclesAsync(device);
    }

    /// <summary>Executes curves, holes and strokes over a separately submitted wgpu background.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task WgpuAlternativeRendererGeometryLoadOraclesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckAlternativeGeometryLoadOraclesAsync(device);
    }

    /// <summary>Checks Vulkan strategy images as filtered backdrops in a parent graph.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task VulkanAlternativeRendererBackdropOraclesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckAlternativeBackdropOraclesAsync(device);
    }

    /// <summary>Checks wgpu strategy images as filtered backdrops in a parent graph.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task WgpuAlternativeRendererBackdropOraclesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckAlternativeBackdropOraclesAsync(device);
    }

    /// <summary>Checks two simultaneously owned retained frames and completion-safe reuse on Vulkan.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task VulkanAlternativeRendererRetainedOraclesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckAlternativeRetainedOraclesAsync(device);
    }

    /// <summary>Checks two simultaneously owned retained frames and completion-safe reuse on wgpu.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task WgpuAlternativeRendererRetainedOraclesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckAlternativeRetainedOraclesAsync(device);
    }

    /// <summary>Checks the independent half-covered nonuniform-stroke fixture on Vulkan.</summary>
    /// <returns>The completed GPU readback.</returns>
    [GpuFact]
    public async Task VulkanSparseNonuniformStrokeOracleAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckSparseNonuniformStrokeOracleAsync(device);
    }

    /// <summary>Checks the independent half-covered nonuniform-stroke fixture on wgpu.</summary>
    /// <returns>The completed GPU readback.</returns>
    [GpuFact]
    public async Task WgpuSparseNonuniformStrokeOracleAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckSparseNonuniformStrokeOracleAsync(device);
    }

    private static async Task CheckSparseNonuniformStrokeOracleAsync(IGraphicDevice device)
    {
        Assert.Equal(0x0FF0U, IndependentArchMask(32, 8));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ConcatTransform(Matrix3x2.CreateScale(16, 0.25f) * Matrix3x2.CreateTranslation(64, 8));
        var path = new PathBuilder2D();
        path.MoveTo(new(-1, 0));
        path.QuadraticTo(new(0, 1), new(1, 0));
        canvas.StrokePath(path.Build(), _white, new() { Width = 6, Cap = LineCap2D.Butt, Join = LineJoin2D.Bevel });
        using IRenderer2D renderer = CreateSparseOracleRenderer(device);
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 128, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        await InitializeOracleBackgroundAsync(device, target, view);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = default });
        Vector4[] pixels = await ReadOracleDirectAsync(device, target, commands =>
        {
            var observed = new PartitionCommands(commands);
            prepared.Record(observed);
            Assert.Equal(0, observed.ComputePasses);
        });

        // Independent analytic 4x4 membership is 0x0FF0, not the N=1 chord's zero.
        AssertPixel(pixels, 128, 32, 8, new(0.5f));
    }

    private static uint IndependentArchMask(int pixelX, int pixelY)
    {
        uint mask = 0;
        for (int j = 0; j < 4; j++)
        {
            for (int i = 0; i < 4; i++)
            {
                double x = (pixelX + ((i + 0.5) / 4) - 64) / 16;
                double y = (pixelY + ((j + 0.5) / 4) - 8) / 0.25;

                // Find interior nearest points using the analytic quadratic's distance
                // derivative, independently of production flattening/subdivision code.
                for (int interval = 0; interval < 64; interval++)
                {
                    double lo = interval / 64.0;
                    double hi = (interval + 1) / 64.0;
                    if (Derivative(lo) >= 0 || Derivative(hi) <= 0)
                    {
                        continue;
                    }

                    for (int iteration = 0; iteration < 60; iteration++)
                    {
                        double middle = (lo + hi) * 0.5;
                        if (Derivative(middle) < 0)
                        {
                            lo = middle;
                        }
                        else
                        {
                            hi = middle;
                        }
                    }

                    double t = (lo + hi) * 0.5;
                    double dx = (2 * t) - 1 - x;
                    double dy = (2 * t * (1 - t)) - y;
                    if ((dx * dx) + (dy * dy) < 9)
                    {
                        mask |= 1U << ((j * 4) + i);
                    }
                }

                double Derivative(double t) => (((2 * t) - 1 - x) * 2) + (((2 * t * (1 - t)) - y) * (2 - (4 * t)));
            }
        }

        return mask;
    }

    private static DrawList2D CreateAlternativeGeometryOracle()
    {
        var canvas = new Canvas2D();
        canvas.Begin();

        // A quadratic arch: its apex is y=4 and the closed base is y=16.
        var quadratic = new PathBuilder2D();
        quadratic.MoveTo(new(2, 16));
        quadratic.QuadraticTo(new(10, -8), new(18, 16));
        quadratic.Close();
        canvas.FillPath(quadratic.Build(), _white);

        // A cubic arch has apex y=3.5 and is well separated from the quadratic.
        var cubic = new PathBuilder2D();
        cubic.MoveTo(new(22, 14));
        cubic.CubicTo(new(22, 0), new(38, 0), new(38, 14));
        cubic.Close();
        canvas.FillPath(cubic.Build(), _red);
        var holes = new PathBuilder2D();
        AddOracleContour(holes, 42, 2, 58, 18, false);
        AddOracleContour(holes, 46, 6, 54, 14, false);
        canvas.FillPath(holes.Build(), _white, FillRule2D.EvenOdd);
        var winding = new PathBuilder2D();
        AddOracleContour(winding, 62, 2, 78, 18, false);
        AddOracleContour(winding, 66, 6, 74, 14, true);
        canvas.FillPath(winding.Build(), _red, FillRule2D.NonZero);
        var stroke = new PathBuilder2D();
        stroke.MoveTo(new(4, 26));
        stroke.LineTo(new(16, 26));
        stroke.LineTo(new(16, 34));
        canvas.StrokePath(stroke.Build(), _white, new() { Width = 4, Cap = LineCap2D.Round, Join = LineJoin2D.Round });
        var curvedStroke = new PathBuilder2D();
        curvedStroke.MoveTo(new(24, 34));
        curvedStroke.QuadraticTo(new(32, 18), new(40, 34));
        canvas.StrokePath(curvedStroke.Build(), _red, new() { Width = 4, Cap = LineCap2D.Butt, Join = LineJoin2D.Round });
        return canvas.End();
    }

    private static void AddOracleContour(PathBuilder2D path, float left, float top, float right, float bottom, bool reverse)
    {
        path.MoveTo(new(left, top));
        path.LineTo(reverse ? new(left, bottom) : new(right, top));
        path.LineTo(new(right, bottom));
        path.LineTo(reverse ? new(right, top) : new(left, bottom));
        path.Close();
    }

    private static async Task CheckAlternativeGeometryLoadOraclesAsync(IGraphicDevice device)
    {
        DrawList2D list = CreateAlternativeGeometryOracle();
        {
            using IRenderer2D renderer = CreateSparseOracleRenderer(device);
            using IGraphicsTexture target = device.CreateTexture(new() { Width = 80, Height = 40, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView view = target.CreateView();

            // This is real prior GPU content, not a ClearColor on the tested prepared draw.
            await InitializeOracleBackgroundAsync(device, target, view);
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Load, ClearColor = _red });
            Vector4[] pixels = await ReadOracleDirectAsync(device, target, prepared.Record);
            Vector4 blue = new(0, 0, 1, 1);
            AssertPixel(pixels, 80, 10, 9, Vector4.One);
            AssertPixel(pixels, 80, 10, 1, blue);
            AssertPixel(pixels, 80, 30, 9, new(1, 0, 0, 1));
            AssertPixel(pixels, 80, 30, 1, blue);
            AssertPixel(pixels, 80, 43, 9, Vector4.One);
            AssertPixel(pixels, 80, 49, 9, blue);
            AssertPixel(pixels, 80, 63, 9, new(1, 0, 0, 1));
            AssertPixel(pixels, 80, 69, 9, blue);
            AssertPixel(pixels, 80, 8, 25, Vector4.One);
            AssertPixel(pixels, 80, 15, 31, Vector4.One);
            AssertPixel(pixels, 80, 0, 26, blue);
            AssertPixel(pixels, 80, 8, 22, blue);
            AssertPixel(pixels, 80, 31, 26, new(1, 0, 0, 1));
            AssertPixel(pixels, 80, 31, 21, blue);
            AssertPixel(pixels, 80, 31, 32, blue);
            AssertPixel(pixels, 80, 79, 39, blue);
        }
    }

    private static async Task InitializeOracleBackgroundAsync(IGraphicDevice device, IGraphicsTexture target, IGraphicsTextureView view)
    {
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
        IRenderEncoder clear = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(0, 0, 1, 1) }] });
        clear.End();
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
    }

    private static async Task<Vector4[]> ReadOracleDirectAsync(IGraphicDevice device, IGraphicsTexture target, Action<IGraphicsCommandBuffer> record)
    {
        uint width = target.Width;
        uint height = target.Height;
        uint pitch = OracleRowPitch(device, width);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * height, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
        record(commands);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = attachment, After = _copyRead });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = width, Height = height }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = height });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        return await DecodeSparseOraclePixelsAsync(readback, width, height, pitch);
    }

    private static uint OracleRowPitch(IGraphicDevice device, uint width)
    {
        TextureCopyLayout layout = device.GetTextureCopyLayout(TextureFormat.Rgba16Float);
        return (((width * 8) + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment;
    }

    private static async Task<Vector4[]> DecodeSparseOraclePixelsAsync(IGraphicsBuffer<byte> readback, uint width, uint height, uint pitch)
    {
        await readback.MapAsync();
        byte[] bytes = new byte[readback.Count];
        try
        {
            readback.CopyTo(bytes);
        }
        finally
        {
            readback.Unmap();
        }

        var pixels = new Vector4[checked((int)(width * height))];
        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                int offset = checked((int)((y * pitch) + (x * 8)));
                pixels[(y * width) + x] = new(ReadHalf(bytes, offset), ReadHalf(bytes, offset + 2), ReadHalf(bytes, offset + 4), ReadHalf(bytes, offset + 6));
            }
        }

        return pixels;
    }

    private static async Task CheckAlternativeBackdropOraclesAsync(IGraphicDevice device)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 8, 4), new SRgba<float>(1, 0, 0, 0.5f));
        canvas.FillRect(new(4, 0, 4, 4), new SRgba<float>(0, 0, 1, 0.5f));
        DrawList2D list = canvas.End();
        {
            using IRenderer2D renderer = CreateSparseOracleRenderer(device);
            using var graph = new Graph(device, "alternative-renderer-backdrop-oracle");
            (PreparedDrawingPasses2D backdropPreparation, EffectImage backdrop) = await AppendSparseOracleImageAsync(graph, renderer, list, 12, 4);
            using PreparedDrawingPasses2D backdropOwner = backdropPreparation;

            // The tested backend supplies the preceding background. Opacity replaces that
            // background, then source-over adds an independently recorded foreground.
            EffectImage filtered = graph.AppendOpacity(backdrop, 0.5f);
            var foreground = new Canvas2D();
            foreground.Begin();
            foreground.FillRect(new(6, 0, 4, 4), new SRgba<float>(1, 1, 1, 0.5f));
            (PreparedDrawingPasses2D sourcePreparation, EffectImage source) = await AppendSparseOracleImageAsync(graph, renderer, foreground.End(), 12, 4);
            using PreparedDrawingPasses2D sourceOwner = sourcePreparation;
            EffectImage output = graph.AppendComposite(source, filtered, EffectCompositeMode.SrcOver);
            renderer.Dispose();
            Vector4[] pixels = await ReadOracleGraphAsync(device, graph, output);
            AssertPixel(pixels, 12, 1, 1, new(0.25f, 0, 0, 0.25f));
            AssertPixel(pixels, 12, 4, 1, new(0.125f, 0, 0.25f, 0.375f));
            AssertPixel(pixels, 12, 6, 1, new(0.5625f, 0.5f, 0.625f, 0.6875f));
            AssertPixel(pixels, 12, 9, 1, new(0.5f));
            AssertPixel(pixels, 12, 11, 1, Vector4.Zero);
        }
    }

    private static async Task<Vector4[]> ReadOracleGraphAsync(IGraphicDevice device, Graph graph, EffectImage image)
    {
        image = graph.AppendOpacity(image, 1);
        uint width = checked((uint)image.Bounds.Width);
        uint height = checked((uint)image.Bounds.Height);
        uint pitch = OracleRowPitch(device, width);
        uint count = pitch * height;
        RenderGraphBuffer<byte> result = graph.CreateBuffer<byte>(new() { Count = count, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }, "oracle-readback");
        graph.AddPass("oracle-readback", context => context.Commands.CopyTextureToBuffer(
            new() { Texture = context.GetTexture(image.Texture), Width = width, Height = height },
            new() { Buffer = context.GetBuffer(result).SliceElements(0, count), BytesPerRow = pitch, RowsPerImage = height }))
            .UseTexture(image.Texture, TextureState.CopySource, _copyRead).UseBuffer(result, _copyWrite);
        graph.ExportBuffer(result, new(PipelineStage.Host, ResourceAccess.HostRead));
        using CompiledRenderGraph plan = graph.Compile();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        plan.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        return await DecodeSparseOraclePixelsAsync(plan.GetBuffer(result), width, height, pitch);
    }

    private static async Task CheckAlternativeRetainedOraclesAsync(IGraphicDevice device)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        var path = new PathBuilder2D();
        path.MoveTo(new(2, 1));
        path.LineTo(new(6, 1));
        path.LineTo(new(6, 3));
        path.LineTo(new(2, 3));
        path.Close();
        canvas.FillPath(path.Build(), PaintGraph2D.Solid(new SRgba<float>(1, 0, 0, 0.5f)));
        using var scene = new DrawingScene2D();
        scene.Insert(scene.Root, 0, canvas.End());
        using DrawingSnapshot2D snapshot = scene.CaptureSnapshot();
        scene.Dispose();
        foreach (int backend in new[] { 0, 1 })
        {
            using IRenderer2D renderer = backend == 0 ? new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault()) : CreateSparseOracleRenderer(device);

            // Prepare two exclusive owners before either retires, then reuse a released slot.
            // In-flight frames have private scratch; only completed assets become reusable.
            using IGraphicsTexture firstTarget = device.CreateTexture(new() { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTexture secondTarget = device.CreateTexture(new() { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView firstView = firstTarget.CreateView();
            using IGraphicsTextureView secondView = secondTarget.CreateView();
            await InitializeOracleBackgroundAsync(device, firstTarget, firstView);
            await InitializeOracleBackgroundAsync(device, secondTarget, secondView);
            using PreparedDrawingFrame2D first = await renderer.PrepareAsync(snapshot, new() { View = firstView, LoadOp = AttachmentLoadOp.Load });
            using PreparedDrawingFrame2D second = await renderer.PrepareAsync(snapshot, new() { View = secondView, LoadOp = AttachmentLoadOp.Load });
            Assert.Equal(2, renderer.RetentionStatistics.ActiveFrames);
            await ExecuteOracleRetainedFrameAsync(device, firstTarget, first);
            Assert.Equal(1, renderer.RetentionStatistics.ActiveFrames);
            await ExecuteOracleRetainedFrameAsync(device, secondTarget, second);
            Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
            using IGraphicsTexture reusedTarget = device.CreateTexture(new() { Width = 8, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView reusedView = reusedTarget.CreateView();
            await InitializeOracleBackgroundAsync(device, reusedTarget, reusedView);
            using PreparedDrawingFrame2D reused = await renderer.PrepareAsync(snapshot, new() { View = reusedView, LoadOp = AttachmentLoadOp.Load });
            Assert.Equal(1, renderer.RetentionStatistics.ActiveFrames);
            Assert.Equal(2, renderer.RetentionStatistics.CompiledRuns);
            Assert.Equal(1, renderer.RetentionStatistics.ReusedRuns);
            await ExecuteOracleRetainedFrameAsync(device, reusedTarget, reused);
            Assert.Equal(0, renderer.RetentionStatistics.ActiveFrames);
        }
    }

    private static async Task ExecuteOracleRetainedFrameAsync(IGraphicDevice device, IGraphicsTexture target, PreparedDrawingFrame2D frame)
    {
        uint pitch = OracleRowPitch(device, 8);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.ColorAttachment, Before = attachment, After = attachment });
        frame.Record(commands);
        Assert.Throws<InvalidOperationException>(() => frame.Record(commands));
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = attachment, After = _copyRead });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 8, Height = 4 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 4 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        frame.AttachSubmission(submission);
        await submission.WaitAsync();
        Assert.Throws<InvalidOperationException>(frame.Retire);
        commands.Dispose();
        frame.Retire();
        Vector4[] pixels = await DecodeSparseOraclePixelsAsync(readback, 8, 4, pitch);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                Vector4 expected = x >= 2 && x < 6 && y >= 1 && y < 3 ? new(0.5f, 0, 0.5f, 1) : new(0, 0, 1, 1);
                AssertPixel(pixels, 8, x, y, expected);
            }
        }
    }
}
