using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks the complete 2D pipeline against independently specified GPU pixel results.</summary>
public sealed class Drawing2DTests
{
    private static readonly Color2D _white = new(1, 1, 1, 1);
    private static readonly Color2D _red = new(1, 0, 0, 1);
    private static readonly Color2D _blue = new(0, 0, 1, 1);
    private static readonly BarrierScope _copyRead = new(PipelineStage.Copy, ResourceAccess.CopyRead);
    private static readonly BarrierScope _copyWrite = new(PipelineStage.Copy, ResourceAccess.CopyWrite);
    private static readonly BarrierScope _sampleRead = new(PipelineStage.VertexShader | PipelineStage.FragmentShader | PipelineStage.ComputeShader, ResourceAccess.ShaderRead);

    /// <summary>Checks geometry, winding, transformed clips and coverage on Vulkan.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task VulkanGeometryAndCoverageAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckGeometryAsync(device);
        await CheckClipsAsync(device);
    }

    /// <summary>Checks geometry, winding, transformed clips and coverage on wgpu.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task WgpuGeometryAndCoverageAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckGeometryAsync(device);
        await CheckClipsAsync(device);
    }

    /// <summary>Checks linear-light paints and all COLRv1 composite modes on Vulkan.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task VulkanPaintAndCompositeAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckGradientsAsync(device);
    }

    /// <summary>Checks linear-light paints and all COLRv1 composite modes on wgpu.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task WgpuPaintAndCompositeAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckGradientsAsync(device);
    }

    /// <summary>Checks standalone preparation and sRGB encoding on Vulkan.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task VulkanStandaloneSrgbTargetAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckStandaloneAsync(device);
    }

    /// <summary>Checks standalone preparation and sRGB encoding on wgpu.</summary>
    /// <returns>The asynchronous GPU checks.</returns>
    [GpuFact]
    public async Task WgpuStandaloneSrgbTargetAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckStandaloneAsync(device);
    }

    private static async Task CheckStandaloneAsync(IGraphicDevice device)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 0, 2, 2), new Color2D(0.5f, 0.5f, 0.5f, 1));
        canvas.FillCircle(Vector2.Zero, 1, new Color2D(0, 0, 0, 0));
        DrawList2D list = canvas.End();
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 4, Height = 2, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        var programs = new ExternalDrawingPrograms();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), programs);
        PreparedDraw2D prepared = await renderer.PrepareAsync(list, new() { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 1, 0.25f) });
        uint pitch = Math.Max(16, device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb).BytesPerRowAlignment);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 2, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        IGraphicsSubmission? submission = null;
        var colorScope = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        try
        {
            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = colorScope });
            prepared.Record(commands);
            Assert.Throws<InvalidOperationException>(() => prepared.Record(commands));
            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = colorScope, After = _copyRead });
            commands.CopyTextureToBuffer(new() { Texture = target, Width = 4, Height = 2 }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 2 });
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            commands.Finish();
            submission = device.Queue.Submit([commands]);
            await submission.WaitAsync();
            await readback.MapAsync();
            byte[] pixels = new byte[checked((int)readback.Count)];
            readback.CopyTo(pixels);
            readback.Unmap();
            for (int y = 0; y < 2; y++)
            {
                int row = checked((int)(y * pitch));
                Assert.InRange(pixels[row + 2], (byte)136, (byte)138);
                Assert.InRange(pixels[row + 3], (byte)63, (byte)64);
                for (int component = 0; component < 3; component++)
                {
                    Assert.InRange(pixels[row + 4 + component], (byte)127, (byte)128);
                }

                Assert.Equal(255, pixels[row + 7]);
            }
        }
        finally
        {
            commands.Dispose();
            Assert.True(programs.Acquired > 0);
            Assert.Equal(0, programs.Returned);
            prepared.Dispose();
            Assert.Equal(programs.Acquired, programs.Returned);
            submission?.Dispose();
        }
    }

    private static async Task CheckGeometryAsync(IGraphicDevice device)
    {
        Vector4[] pixels = await RenderAsync(device, 40, 20, canvas =>
        {
            canvas.FillRoundedRect(new(23, 1, 8, 8), 3, _white);
            canvas.FillCircle(new(5, 15), 3, _white);
        });
        AssertPixel(pixels, 40, 27, 5, Vector4.One);
        AssertPixel(pixels, 40, 23, 1, Vector4.Zero, 0.03f);
        AssertPixel(pixels, 40, 4, 14, Vector4.One);
        Assert.InRange(pixels[(13 * 40) + 7].W, 0.2f, 0.9f);
        AssertPixel(pixels, 40, 1, 11, Vector4.Zero);
    }

    private static async Task CheckClipsAsync(IGraphicDevice device)
    {
        Vector4[] correlated = await RenderAsync(device, 8, 4, canvas =>
        {
            canvas.ClipRect(new(0.5f, 0, 6, 4));
            canvas.FillRect(new(0.5f, 0, 6, 4), PaintGraph2D.Clip(Rectangle(0.5f, 0, 6, 4), PaintGraph2D.Solid(_white)));
        });
        AssertPixel(correlated, 8, 0, 1, new(0.5f));
        AssertPixel(correlated, 8, 1, 1, Vector4.One);
        AssertPixel(correlated, 8, 6, 1, new(0.5f));
        AssertPixel(correlated, 8, 7, 1, Vector4.Zero);

        Vector4[] transformed = await RenderAsync(
            device,
            6,
            4,
            canvas =>
        {
            canvas.Save();
            canvas.ConcatTransform(new(1, 0, 1, 1, 0, 0));
            canvas.ClipRect(new(0, 0, 2, 2));
            canvas.FillRect(new(-10, -10, 20, 20), _white);
            canvas.Restore();
            canvas.Translate(new(2, 1));
            canvas.FillRect(new(0, 0, 0.5f, 0.5f), _red);
        },
            pixelScale: 2);
        AssertPixel(transformed, 6, 0, 2, Vector4.Zero);
        AssertPixel(transformed, 6, 3, 2, Vector4.One);
        AssertPixel(transformed, 6, 4, 2, new(1, 0, 0, 1));
    }

    private static async Task CheckGradientsAsync(IGraphicDevice device)
    {
        ColorStop2D[] transparentBlueToRed = [new(0, new(0, 0, 1, 0)), new(1, _red)];
        ColorStop2D[] blackToWhite = [new(0, new(0, 0, 0, 1)), new(1, _white)];
        Vector4[] pixels = await RenderAsync(device, 8, 4, canvas =>
        {
            canvas.FillRect(new(0, 0, 8, 1), PaintGraph2D.LinearGradient(Vector2.Zero, new(8, 0), transparentBlueToRed));
            canvas.FillRect(new(0, 1, 8, 1), PaintGraph2D.LinearGradient(Vector2.Zero, new(4, 0), new(4, 4), blackToWhite, ExtendMode2D.Pad));
            canvas.FillRect(new(0, 2, 8, 1), new Color2D(0.5f, 0.5f, 0.5f, 1));
            canvas.FillRect(new(0, 3, 8, 1), PaintGraph2D.LinearGradient(Vector2.Zero, new(4, 0), blackToWhite, ExtendMode2D.Reflect));
        });
        AssertPixel(pixels, 8, 3, 0, new(0.4375f, 0, 0, 0.4375f));
        AssertPixel(pixels, 8, 3, 1, new(0.5f, 0.5f, 0.5f, 1));
        AssertPixel(pixels, 8, 3, 2, new(0.21404114f, 0.21404114f, 0.21404114f, 1));
        AssertPixel(pixels, 8, 4, 3, new(0.875f, 0.875f, 0.875f, 1));

        Vector4[] otherGradients = await RenderAsync(device, 12, 4, canvas =>
        {
            canvas.FillRect(new(0, 0, 4, 4), PaintGraph2D.RadialGradient(new(0.5f, 0.5f), 4, blackToWhite));
            canvas.FillRect(new(4, 0, 4, 4), PaintGraph2D.SweepGradient(new(4.5f, 0.5f), 0, MathF.PI, blackToWhite, ExtendMode2D.Pad));
            canvas.FillRect(new(8, 0, 4, 4), PaintGraph2D.RadialGradient(new(8, 0.5f), 1, new(9, 0.5f), 3, blackToWhite, ExtendMode2D.Pad));
        });
        AssertPixel(otherGradients, 12, 2, 0, new(0.5f, 0.5f, 0.5f, 1), 0.02f);
        AssertPixel(otherGradients, 12, 4, 2, new(0.5f, 0.5f, 0.5f, 1), 0.02f);
        AssertPixel(otherGradients, 12, 10, 0, new(0.5f, 0.5f, 0.5f, 1), 0.02f);
    }

    private static async Task<Vector4[]> RenderAsync(IGraphicDevice device, int width, int height, Action<Canvas2D> draw, IGraphicsTexture[]? inputs = null, float pixelScale = 1)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        draw(canvas);
        using IGraphicsTexture target = device.CreateTexture(new() { Width = (uint)width, Height = (uint)height, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        var programs = new ExternalDrawingPrograms();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault(), programs);
        using PreparedDraw2D drawing = await renderer.PrepareAsync(canvas.End(), new() { View = view, LoadOp = AttachmentLoadOp.Clear, PixelScale = pixelScale });
        TextureCopyLayout layout = device.GetTextureCopyLayout(target.Format);
        uint rowBytes = checked((uint)width * layout.BytesPerTexel);
        uint pitch = checked(((rowBytes + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment);
        uint byteCount = checked(pitch * (uint)height);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = byteCount, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var colorScope = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = colorScope });
        drawing.Record(commands);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = colorScope, After = _copyRead });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = (uint)width, Height = (uint)height }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = (uint)height });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] bytes = new byte[byteCount];
        readback.CopyTo(bytes);
        readback.Unmap();
        var pixels = new Vector4[checked(width * height)];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = checked((int)((y * pitch) + (x * 8)));
                pixels[(y * width) + x] = new(ReadHalf(bytes, offset), ReadHalf(bytes, offset + 2), ReadHalf(bytes, offset + 4), ReadHalf(bytes, offset + 6));
            }
        }

        commands.Dispose();
        Assert.True(programs.Acquired > 0);
        Assert.Equal(0, programs.Returned);
        drawing.Dispose();
        Assert.Equal(programs.Acquired, programs.Returned);
        return pixels;
    }

    private static Path2D Rectangle(float x, float y, float width, float height)
    {
        var builder = new PathBuilder2D();
        AddRectangle(builder, x, y, width, height);
        return builder.Build();
    }

    private static void AddRectangle(PathBuilder2D builder, float x, float y, float width, float height, bool reverse = false)
    {
        builder.MoveTo(new(x, y));
        if (reverse)
        {
            builder.LineTo(new(x, y + height));
            builder.LineTo(new(x + width, y + height));
            builder.LineTo(new(x + width, y));
        }
        else
        {
            builder.LineTo(new(x + width, y));
            builder.LineTo(new(x + width, y + height));
            builder.LineTo(new(x, y + height));
        }

        builder.Close();
    }

    private static float ReadHalf(byte[] bytes, int offset) => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)));

    private static void AssertPixel(Vector4[] pixels, int width, int x, int y, Vector4 expected, float tolerance = 0.002f)
    {
        Vector4 actual = pixels[(y * width) + x];
        var difference = Vector4.Abs(actual - expected);
        Assert.True(difference.X <= tolerance && difference.Y <= tolerance && difference.Z <= tolerance && difference.W <= tolerance, $"Pixel ({x}, {y}): expected {expected}, actual {actual}, tolerance {tolerance}.");
    }

    private sealed class ExternalDrawingPrograms : IGraphicsProgramProvider
    {
        private readonly ShaderArtifact _vertex = new(ShaderArtifact.LoadEmbedded(typeof(DrawingPrograms2D).Assembly, "Lumyte.Drawing2D.drawing-vertex.lshader").GetBinary());
        private readonly ShaderArtifact _fragment = new(ShaderArtifact.LoadEmbedded(typeof(DrawingPrograms2D).Assembly, "Lumyte.Drawing2D.drawing-fragment.lshader").GetBinary());

        internal int Acquired { get; private set; }

        internal int Returned { get; private set; }

        public GraphicsProgramLease Acquire(IGraphicDevice device, string programId)
        {
            Assert.Equal(DrawingPrograms2D.ProgramId, programId);
            var native = GraphicsProgramLease.Create(device, _vertex, _fragment);
            Acquired++;
            return new(native.Resources, () =>
            {
                Returned++;
                native.Dispose();
            });
        }
    }
}
