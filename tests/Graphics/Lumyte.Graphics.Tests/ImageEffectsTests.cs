using System.Buffers.Binary;
using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks shared image effects independently of the 2D drawing and layer implementations.</summary>
public sealed class ImageEffectsTests
{
    /// <summary>Checks matrix algebra, finite composite gaps, and two-axis convolution on Vulkan.</summary>
    /// <returns>The asynchronous GPU readback checks.</returns>
    [GpuFact]
    public async Task VulkanImageEffectReadbackAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckMatrixAsync(device);
        await CheckCompositeGapAsync(device);
        await CheckLargeOriginOffsetAsync(device);
        await CheckTwoAxisBlurAsync(device);
    }

    /// <summary>Checks matrix algebra, finite composite gaps, and two-axis convolution on wgpu.</summary>
    /// <returns>The asynchronous GPU readback checks.</returns>
    [GpuFact]
    public async Task WgpuImageEffectReadbackAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckMatrixAsync(device);
        await CheckCompositeGapAsync(device);
        await CheckLargeOriginOffsetAsync(device);
        await CheckTwoAxisBlurAsync(device);
    }

    private static async Task CheckMatrixAsync(IGraphicDevice device)
    {
        // Premultiplied input (.2, -.1, .3, .25) represents straight (.8, -.4, 1.2, .25).
        // The matrix scales straight R by four and sets alpha to one half.
        float[] coefficients =
        [
            4, 0, 0, 0, 0,
            0, 1, 0, 0, 0,
            0, 0, 1, 0, 0,
            0, 0, 0, 0, 0.5f,
        ];
        var origin = new EffectPixelRect(16_777_217, -16_777_219, 1, 1);
        var source = ImageEffectGraph.Source();
        (EffectPixelRect bounds, Vector4[] pixels) = await RenderAsync(device, ImageEffectGraph.ColorMatrix(new(coefficients), source), origin, new(0.2f, -0.1f, 0.3f, 0.25f));
        Assert.Equal(origin, bounds);
        AssertPixel(pixels[0], new(1.6f, -0.2f, 0.6f, 0.5f));

        // Only alpha is clamped; RGB remains HDR and may be negative.
        coefficients[19] = 2;
        (_, pixels) = await RenderAsync(device, ImageEffectGraph.ColorMatrix(new(coefficients), source), origin, new(0.2f, -0.1f, 0.3f, 0.25f));
        AssertPixel(pixels[0], new(3.2f, -0.4f, 1.2f, 1));
    }

    private static async Task CheckCompositeGapAsync(IGraphicDevice device)
    {
        var source = ImageEffectGraph.Source();
        var separated = ImageEffectGraph.Composite(source, source, EffectCompositeMode.SrcOver, 4, 0);
        var constants = new ColorMatrix4x5(
        [
            0, 0, 0, 0, 1.5f,
            0, 0, 0, 0, -0.25f,
            0, 0, 0, 0, 0.125f,
            0, 0, 0, 0, 0.5f,
        ]);
        (EffectPixelRect bounds, Vector4[] pixels) = await RenderAsync(device, ImageEffectGraph.ColorMatrix(constants, separated), new(-17, 9, 1, 1), Vector4.One);
        Assert.Equal(new EffectPixelRect(-17, 9, 5, 1), bounds);
        Assert.Equal(5, pixels.Length);
        foreach (Vector4 pixel in pixels)
        {
            // Constants affect the three transparent pixels between the separated children, too.
            AssertPixel(pixel, new(0.75f, -0.125f, 0.0625f, 0.5f));
        }
    }

    private static async Task CheckLargeOriginOffsetAsync(IGraphicDevice device)
    {
        var source = ImageEffectGraph.Source();
        var shifted = ImageEffectGraph.Composite(ImageEffectGraph.Opacity(0.25f, source), source, EffectCompositeMode.SrcOver, 1, 0);
        (EffectPixelRect bounds, Vector4[] pixels) = await RenderAsync(device, shifted, new(16_777_219, -16_777_219, 1, 1), Vector4.One);
        Assert.Equal(new EffectPixelRect(16_777_219, -16_777_219, 2, 1), bounds);
        AssertPixel(pixels[0], Vector4.One);
        AssertPixel(pixels[1], new(0.25f));
    }

    private static async Task CheckTwoAxisBlurAsync(IGraphicDevice device)
    {
        var blur = ImageEffectGraph.GaussianBlur(1, 2, ImageEffectGraph.Source());
        (EffectPixelRect bounds, Vector4[] pixels) = await RenderAsync(device, blur, new(16_777_217, -16_777_219, 1, 1), Vector4.One);
        Assert.Equal(new EffectPixelRect(16_777_214, -16_777_225, 7, 13), bounds);

        // Product of independently normalized radius-three and radius-six Gaussian centers.
        AssertPixel(pixels[(6 * 7) + 3], new(0.07968062f), 0.0002f);
        Assert.InRange(pixels.Sum(pixel => pixel.W), 0.997f, 1.003f);
        Assert.InRange(pixels[0].W, 0.000009f, 0.000011f);
        for (int y = 0; y < 13; y++)
        {
            for (int x = 0; x < 7; x++)
            {
                AssertPixel(pixels[(y * 7) + x], pixels[((12 - y) * 7) + (6 - x)], 0.00002f);
            }
        }
    }

    private static async Task<(EffectPixelRect Bounds, Vector4[] Pixels)> RenderAsync(IGraphicDevice device, ImageEffectGraph effect, EffectPixelRect sourceBounds, Vector4 sourceColor)
    {
        var graph = new Graph(device, "standalone-image-effect-readback");
        RenderGraphTexture source = graph.CreateTexture(
            new()
            {
                Width = checked((uint)sourceBounds.Width),
                Height = checked((uint)sourceBounds.Height),
                Format = TextureFormat.Rgba16Float,
                Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled,
            },
            "source");
        graph.AddPass("clear-source", context =>
        {
            IRenderEncoder clear = context.Commands.BeginRenderPass(new()
            {
                ColorAttachments = [new() { View = context.CreateTextureView(source), LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(sourceColor.X, sourceColor.Y, sourceColor.Z, sourceColor.W) }],
            });
            clear.End();
        }).UseTexture(source, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        var compiler = new ImageEffectCompiler(device, ImageEffectRenderers.CreateDefault());
        PreparedImageEffect prepared = compiler.AppendPasses(graph, effect, new(source, sourceBounds, EffectColorSemantic.SceneLinear));
        Assert.Equal(EffectColorSemantic.SceneLinear, prepared.Output.ColorSemantic);
        EffectPixelRect bounds = prepared.Output.Bounds;
        uint width = checked((uint)bounds.Width);
        uint height = checked((uint)bounds.Height);
        TextureCopyLayout layout = device.GetTextureCopyLayout(TextureFormat.Rgba16Float);
        uint rowBytes = checked(width * layout.BytesPerTexel);
        uint pitch = checked(((rowBytes + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment);
        uint byteCount = checked(pitch * height);
        RenderGraphBuffer<byte> readback = graph.CreateBuffer<byte>(new() { Count = byteCount, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }, "readback");
        graph.AddPass("readback", context => context.Commands.CopyTextureToBuffer(
            new() { Texture = context.GetTexture(prepared.Output.Texture), Width = width, Height = height },
            new() { Buffer = context.GetBuffer(readback).Slice(0, byteCount), BytesPerRow = pitch, RowsPerImage = height }))
            .UseTexture(prepared.Output.Texture, TextureState.CopySource, new(PipelineStage.Copy, ResourceAccess.CopyRead))
            .UseBuffer(readback, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        graph.ExportBuffer(readback, new(PipelineStage.Host, ResourceAccess.HostRead));
        CompiledRenderGraph? plan = null;
        IGraphicsCommandBuffer? commands = null;
        IGraphicsSubmission? submission = null;
        try
        {
            plan = graph.Compile();
            commands = device.CreateCommandBuffer(new());
            plan.Record(commands);
            commands.Finish();
            submission = device.Queue.Submit([commands]);
            await submission.WaitAsync();
            IGraphicsBuffer<byte> buffer = plan.GetBuffer(readback);
            await buffer.MapAsync();
            byte[] bytes = new byte[byteCount];
            buffer.CopyTo(bytes);
            buffer.Unmap();
            var pixels = new Vector4[checked(bounds.Width * bounds.Height)];
            for (int y = 0; y < bounds.Height; y++)
            {
                for (int x = 0; x < bounds.Width; x++)
                {
                    int offset = checked((int)((y * pitch) + (x * 8)));
                    pixels[(y * bounds.Width) + x] = new(ReadHalf(bytes, offset), ReadHalf(bytes, offset + 2), ReadHalf(bytes, offset + 4), ReadHalf(bytes, offset + 6));
                }
            }

            return (bounds, pixels);
        }
        finally
        {
            commands?.Dispose();
            plan?.Dispose();
            prepared.Dispose();
            submission?.Dispose();
        }
    }

    private static float ReadHalf(byte[] bytes, int offset) => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)));

    private static void AssertPixel(Vector4 actual, Vector4 expected, float tolerance = 0.002f)
    {
        var difference = Vector4.Abs(actual - expected);
        Assert.True(difference.X <= tolerance && difference.Y <= tolerance && difference.Z <= tolerance && difference.W <= tolerance, $"Expected {expected}; got {actual}.");
    }
}
