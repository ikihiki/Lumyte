using System.Numerics;
using System.Reflection;
using Lumyte.Graphics;

namespace Lumyte.Samples;

internal static class TwentySquaresScene
{
    internal const int Columns = 5;
    internal const int Rows = 4;
    internal const int SquareSize = 32;
    internal const int SquareCount = Columns * Rows;
    internal const int Width = Columns * SquareSize;
    internal const int Height = Rows * SquareSize;
    internal const uint BytesPerRow = ((Width * 4) + 255) / 256 * 256;

    // Exact RGBA8 texels. Material coefficients do not supply the colors.
    internal static ReadOnlySpan<uint> Colors =>
    [
        0xff0000, 0x00ff00, 0x0000ff, 0xffff00, 0xff00ff,
        0x00ffff, 0xff8000, 0x8000ff, 0x80ff00, 0x00ff80,
        0x0080ff, 0xff0080, 0x804000, 0x808000, 0x008080,
        0x800080, 0x808080, 0xffffff, 0xffb080, 0x404040,
    ];

    internal static byte[] Run(GraphicsDevice device, Assembly shaders)
    {
        // This consumer owns every allocation. Unwind in reverse order, including on validation failure.
        var resources = new List<IDisposable>();
        try
        {
            Sampler sampler = Own(device.CreateSampler(new SamplerDesc { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest }), resources);
            ShaderModule shader = Own(device.CreateShader(shaders, "Lumyte.Shaders.squares.wgsl"), resources);
            GraphicsPipeline pipeline = Own(device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader }), resources);
            ShaderDataLayout<SquareMaterial> layout = shader.GetDataLayout<SquareMaterial>();
            var textureUploads = new IGraphicsBuffer<byte>[SquareCount];
            var textures = new IGraphicsTexture[SquareCount];
            var views = new IGraphicsTextureView[SquareCount];
            for (int i = 0; i < SquareCount; i++)
            {
                textures[i] = Own(device.CreateTexture(new TextureDesc { Width = 1, Height = 1, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination }), resources);
                views[i] = Own(textures[i].CreateView(), resources);
                textureUploads[i] = Own(device.CreateBuffer(new BufferDesc<byte> { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }), resources);
                textureUploads[i].CopyFrom(ColorBytes(i));
            }

            IArgumentTable table = Own(device.CreateArgumentTable(new ArgumentTableDesc { TextureCapacity = SquareCount, SamplerCapacity = 1 }), resources);
            IGpuRef<Sampler> samplerReference = table.WriteSampler(0, sampler);
            var materials = new SquareMaterial[SquareCount];
            for (int square = 0; square < SquareCount; square++)
            {
                IGpuRef<IGraphicsTextureView> textureReference = table.WriteTexture((uint)square, views[square]);
                int column = square % Columns;
                int row = square / Columns;
                var rectangle = new Vector4(-1f + (2f * column / Columns), 1f - (2f * (row + 1) / Rows), 2f / Columns, 2f / Rows);
                materials[square] = new(rectangle, textureReference, samplerReference);
            }

            ulong byteCount = layout.GetSizeInBytes(SquareCount);
            IGraphicsBuffer<byte> materialUpload = Own(device.CreateBuffer(new BufferDesc<byte> { Count = byteCount, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }), resources);
            IGraphicsBuffer<byte> materialBuffer = Own(device.CreateBuffer(new BufferDesc<byte> { Count = byteCount, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }), resources);
            materialUpload.Slice(0, byteCount).CopyFrom<SquareMaterial>(materials, layout, SquareMaterialSerializer.Instance);

            // The sample explicitly records all texture and material uploads, submits, and observes completion.
            using (CommandEncoder transfer = device.CreateCommandEncoder())
            {
                for (int i = 0; i < SquareCount; i++)
                {
                    transfer.RecordCopyBufferToTexture(textureUploads[i].Slice(0, textureUploads[i].Count), textures[i], 256);
                }

                transfer.RecordCopyBuffer(materialUpload.Slice(0, byteCount), materialBuffer.Slice(0, byteCount));

                using CommandBuffer commands = transfer.Finish();
                device.Submit(commands).Wait();
            }

            IArgumentTable roots = Own(device.CreateArgumentTable(new ArgumentTableDesc { BufferCapacity = 1 }), resources);
            IGpuRef<SquareMaterial> materialReference = roots.WriteBuffer(0, materialBuffer.Slice(0, byteCount), layout);
            var arguments = new ShaderArguments[SquareCount];
            for (int square = 0; square < SquareCount; square++)
            {
                // Root data identifies one element. The backend follows its recorded descriptor dependencies.
                arguments[square] = Own(pipeline.CreateArguments(materialReference.GetElement((ulong)square)), resources);
            }

            IGraphicsTexture target = Own(device.CreateTexture(new TextureDesc { Width = Width, Height = Height }), resources);
            IGraphicsTextureView targetView = Own(target.CreateView(), resources);
            IGraphicsBuffer<byte> readback = Own(device.CreateBuffer(new BufferDesc<byte> { Count = Height * BytesPerRow, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }), resources);
            using (CommandEncoder encoder = device.CreateCommandEncoder())
            {
                using (RenderEncoder render = encoder.BeginRenderPass(new RenderPassDesc { Target = targetView, ClearValue = new Color4(0, 0, 0, 0) }))
                {
                    render.SetPipeline(pipeline);
                    for (int square = 0; square < SquareCount; square++)
                    {
                        render.Draw(arguments[square], new DrawDesc { VertexCount = 6 });
                    }
                }

                encoder.RecordCopyTextureToBuffer(target, readback, BytesPerRow);
                using CommandBuffer commands = encoder.Finish();
                device.Submit(commands).Wait();
            }

            byte[] pixels = new byte[Height * BytesPerRow];
            readback.CopyTo(pixels);
            return pixels;
        }
        finally
        {
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                resources[i].Dispose();
            }
        }
    }

    internal static void Verify(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != Height * BytesPerRow)
        {
            throw new ArgumentException("Unexpected padded readback size.", nameof(pixels));
        }

        for (int square = 0; square < SquareCount; square++)
        {
            byte[] expected = ColorBytes(square);
            int originX = (square % Columns) * SquareSize;
            int originY = (square / Columns) * SquareSize;
            for (int y = originY; y < originY + SquareSize; y++)
            {
                for (int x = originX; x < originX + SquareSize; x++)
                {
                    if (!pixels.Slice(checked((int)((y * BytesPerRow) + (x * 4))), 4).SequenceEqual(expected))
                    {
                        throw new InvalidOperationException($"Square {square} texture color mismatch at ({x}, {y}).");
                    }
                }
            }
        }
    }

    internal static byte[] ColorBytes(int index)
    {
        uint color = Colors[index];
        return [(byte)(color >> 16), (byte)(color >> 8), (byte)color, 255];
    }

    private static T Own<T>(T resource, List<IDisposable> resources)
        where T : IDisposable
    {
        resources.Add(resource);
        return resource;
    }
}
