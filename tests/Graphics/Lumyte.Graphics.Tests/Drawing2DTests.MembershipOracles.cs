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

/// <summary>Strict historical analytic oracles for explicit membership processing.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Executes curves, holes and strokes over a separately submitted Vulkan background.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task VulkanMembershipRendererGeometryLoadOraclesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckMembershipGeometryLoadOraclesAsync(device);
    }

    /// <summary>Executes curves, holes and strokes over a separately submitted wgpu background.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task WgpuMembershipRendererGeometryLoadOraclesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckMembershipGeometryLoadOraclesAsync(device);
    }

    /// <summary>Checks Vulkan strategy images as filtered backdrops in a parent graph.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task VulkanMembershipRendererBackdropOraclesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckMembershipBackdropOraclesAsync(device);
    }

    /// <summary>Checks wgpu strategy images as filtered backdrops in a parent graph.</summary>
    /// <returns>The completed GPU checks.</returns>
    [GpuFact]
    public async Task WgpuMembershipRendererBackdropOraclesAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckMembershipBackdropOraclesAsync(device);
    }

    /// <summary>Checks the independent half-covered nonuniform-stroke fixture on Vulkan.</summary>
    /// <returns>The completed GPU readback.</returns>
    [GpuFact]
    public async Task VulkanMembershipNonuniformStrokeOracleAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckMembershipNonuniformStrokeOracleAsync(device);
    }

    /// <summary>Checks the independent half-covered nonuniform-stroke fixture on wgpu.</summary>
    /// <returns>The completed GPU readback.</returns>
    [GpuFact]
    public async Task WgpuMembershipNonuniformStrokeOracleAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckMembershipNonuniformStrokeOracleAsync(device);
    }

    /// <summary>Checks standard raw shader-data streams across Vulkan graph passes.</summary>
    /// <returns>The completed GPU readback.</returns>
    [GpuFact]
    public async Task VulkanMembershipCurveGraphOracleAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckMembershipCurveGraphOracleAsync(device);
    }

    /// <summary>Checks reference-free shader data does not fuse wgpu compute/raster tables.</summary>
    /// <returns>The completed GPU readback.</returns>
    [GpuFact]
    public async Task WgpuMembershipCurveGraphOracleAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckMembershipCurveGraphOracleAsync(device);
    }

    private static async Task CheckMembershipNonuniformStrokeOracleAsync(IGraphicDevice device)
    {
        Assert.Equal(0x0FF0U, IndependentArchMask(32, 8));
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.ConcatTransform(Matrix3x2.CreateScale(16, 0.25f) * Matrix3x2.CreateTranslation(64, 8));
        var path = new PathBuilder2D();
        path.MoveTo(new(-1, 0));
        path.QuadraticTo(new(0, 1), new(1, 0));
        canvas.StrokePath(path.Build(), _white, new() { Width = 6, Cap = LineCap2D.Butt, Join = LineJoin2D.Bevel });
        using IRenderer2D renderer = CreateMembershipOracleRenderer(device);
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 128, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        await InitializeOracleBackgroundAsync(device, target, view);
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = default });
        Vector4[] pixels = await ReadOracleDirectAsync(device, target, commands =>
        {
            var observed = new PartitionCommands(commands);
            prepared.Record(observed);
            Assert.Equal(1, observed.ComputePasses);
            Assert.Equal(1, observed.ComputeDispatches);
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

    private static async Task CheckMembershipCurveGraphOracleAsync(IGraphicDevice device)
    {
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        var path = new PathBuilder2D();
        path.MoveTo(new(0, 0));
        path.QuadraticTo(new(4, 0), new(4, 4));
        path.LineTo(new(0, 4));
        path.Close();
        canvas.FillPath(path.Build(), _red);
        using IRenderer2D renderer = CreateMembershipOracleRenderer(device);
        using var graph = new Graph(device, "standard-reference-free-curve-graph-oracle");
        (EffectImage image, PreparedDrawingPasses2D drawing) = await AppendMembershipImageAsync(graph, renderer, canvas.End(), new(0, 0, 8, 8));
        using IDisposable drawingOwner = drawing;
        EffectImage output = graph.AppendOpacity(image, 0.5f);
        Vector4[] pixels = await ReadOracleGraphAsync(device, graph, output);
        AssertPixel(pixels, 8, 1, 1, new(0.5f, 0, 0, 0.5f));
        AssertPixel(pixels, 8, 7, 7, Vector4.Zero);
    }

    private static IRenderer2D CreateOracleRenderer(IGraphicDevice device, bool compute)
        => compute ? CreateMembershipOracleRenderer(device) : new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());

    private static DrawList2D CreateMembershipGeometryOracle()
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

    private static async Task CheckMembershipGeometryLoadOraclesAsync(IGraphicDevice device)
    {
        DrawList2D list = CreateMembershipGeometryOracle();
        Vector4[]? control = null;
        foreach (bool compute in new[] { false, true })
        {
            using IRenderer2D renderer = CreateOracleRenderer(device, compute);
            using IGraphicsTexture target = device.CreateTexture(new() { Width = 80, Height = 40, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            using IGraphicsTextureView view = target.CreateView();

            // This is real prior GPU content, not a ClearColor on the tested prepared draw.
            await InitializeOracleBackgroundAsync(device, target, view);
            using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Load, ClearColor = _red });
            Vector4[] pixels = await ReadOracleDirectAsync(device, target, prepared.Record);
            if (control is not null)
            {
                for (int y = 0; y < 40; y++)
                {
                    for (int x = 0; x < 80; x++)
                    {
                        AssertPixel(pixels, 80, x, y, control[(y * 80) + x]);
                    }
                }
            }
            else
            {
                control = pixels;
            }

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
        return await DecodeMembershipOraclePixelsAsync(readback, width, height, pitch);
    }

    private static uint OracleRowPitch(IGraphicDevice device, uint width)
    {
        TextureCopyLayout layout = device.GetTextureCopyLayout(TextureFormat.Rgba16Float);
        return (((width * 8) + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment;
    }

    private static async Task<Vector4[]> DecodeMembershipOraclePixelsAsync(IGraphicsBuffer<byte> readback, uint width, uint height, uint pitch)
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

    private static async Task CheckMembershipBackdropOraclesAsync(IGraphicDevice device)
    {
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 8, 4), new SRgba<float>(1, 0, 0, 0.5f));
        canvas.FillRect(new(4, 0, 4, 4), new SRgba<float>(0, 0, 1, 0.5f));
        RenderGraphDrawList2D list = canvas.End();
        foreach (bool compute in new[] { false, true })
        {
            using IRenderer2D renderer = CreateOracleRenderer(device, compute);
            using var graph = new Graph(device, "alternative-renderer-backdrop-oracle");
            (EffectImage backdrop, PreparedDrawingPasses2D backgroundDrawing) = await AppendMembershipImageAsync(graph, renderer, list, new(0, 0, 12, 4));
            using IDisposable backgroundOwner = backgroundDrawing;

            // The tested backend supplies the preceding background. Opacity replaces that
            // background, then source-over adds an independently recorded foreground.
            EffectImage filtered = graph.AppendOpacity(backdrop, 0.5f);
            var foreground = new RenderGraphCanvas2D();
            foreground.Begin();
            foreground.FillRect(new(6, 0, 4, 4), new SRgba<float>(1, 1, 1, 0.5f));
            (EffectImage source, PreparedDrawingPasses2D foregroundDrawing) = await AppendMembershipImageAsync(graph, renderer, foreground.End(), new(0, 0, 12, 4));
            using IDisposable foregroundOwner = foregroundDrawing;
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

    private static async Task<(EffectImage Image, PreparedDrawingPasses2D Owner)> AppendMembershipImageAsync(Graph graph, IRenderer2D renderer, RenderGraphDrawList2D list, EffectPixelRect bounds, IReadOnlyList<IGraphicsTexture>? inputs = null)
    {
        var bindings = new DrawingGraphBindings2D(graph);
        foreach (IGraphicsTexture input in inputs ?? [])
        {
            bindings.BindTexture(input, graph.ImportTexture(input, TextureState.Sampled, _sampleRead));
        }

        var description = new TextureDesc
        {
            Width = checked((uint)bounds.Width),
            Height = checked((uint)bounds.Height),
            Format = TextureFormat.Rgba16Float,
            Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled | TextureUsage.CopySource,
        };
        RenderGraphTexture target = graph.CreateTexture(description, "membership-oracle");
        using var graphRenderer = new RenderGraphRenderer2D(renderer, list, new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, bindings);
        PreparedDrawingPasses2D owner = await graph.Append2D(graphRenderer);
        return (new(target, bounds, EffectColorSemantic.DisplayLinear), owner);
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
        return await DecodeMembershipOraclePixelsAsync(plan.GetBuffer(result), width, height, pitch);
    }
}
