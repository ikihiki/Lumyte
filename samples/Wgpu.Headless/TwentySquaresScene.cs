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
            MaterialResourceLayout layout = shader.GetMaterialResourceLayout();
            int capacity = checked((int)layout.PairCapacity);
            if (capacity != 4)
            {
                throw new NotSupportedException("This sample uses the compiled four-pair profile.");
            }

            var references = new SampledTexture2DReference[SquareCount];
            var textureUploads = new IGraphicsBuffer<byte>[SquareCount];
            var textures = new IGraphicsTexture[SquareCount];
            for (int i = 0; i < SquareCount; i++)
            {
                textures[i] = Own(device.CreateTexture(new TextureDesc { Width = 1, Height = 1, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination }), resources);
                IGraphicsTextureView view = Own(textures[i].CreateView(), resources);
                references[i] = device.CreateSampledTexture2DReference(view, sampler);
                textureUploads[i] = Own(device.CreateBuffer(new BufferDesc<byte> { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }), resources);
                textureUploads[i].CopyFrom(ColorBytes(i));
            }

            int batchCount = (SquareCount + capacity - 1) / capacity;
            var materialUploads = new IGraphicsBuffer<byte>[batchCount];
            var materialBuffers = new IGraphicsBuffer<byte>[batchCount];
            uint[] instanceCounts = new uint[batchCount];
            for (int batch = 0; batch < batchCount; batch++)
            {
                int first = batch * capacity;
                int count = Math.Min(capacity, SquareCount - first);
                var materials = new SquareMaterial[count];
                for (int i = 0; i < count; i++)
                {
                    int square = first + i;
                    int column = square % Columns;
                    int row = square / Columns;
                    var rectangle = new Vector4(-1f + (2f * column / Columns), 1f - (2f * (row + 1) / Rows), 2f / Columns, 2f / Rows);
                    materials[i] = new(rectangle, references[square]);
                }

                // Fallback is the first used pair, so all four slots can hold distinct textures.
                IGraphicsMaterialBindings bindings = Own(
                    device.CreateMaterialBindings<SquareMaterial>(
                    new MaterialBindingsDesc { Layout = layout, UnusedSlotFallback = references[first] },
                    materials,
                    SquareMaterialSerializer.Instance),
                    resources);
                materialUploads[batch] = Own(device.CreateBuffer(new BufferDesc<byte> { Count = bindings.SizeInBytes, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }), resources);
                materialBuffers[batch] = Own(device.CreateBuffer(new BufferDesc<byte> { Count = bindings.SizeInBytes, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }), resources);
                materialUploads[batch].Slice(0, bindings.SizeInBytes).CopyFrom(bindings);
                instanceCounts[batch] = checked((uint)count);
            }

            // The sample explicitly records all texture and material uploads, submits, and observes completion.
            using (CommandEncoder transfer = device.CreateCommandEncoder())
            {
                for (int i = 0; i < SquareCount; i++)
                {
                    transfer.RecordCopyBufferToTexture(textureUploads[i].Slice(0, textureUploads[i].Count), textures[i], 256);
                }

                for (int batch = 0; batch < batchCount; batch++)
                {
                    transfer.RecordCopyBuffer(materialUploads[batch].Slice(0, materialUploads[batch].Count), materialBuffers[batch].Slice(0, materialBuffers[batch].Count));
                }

                using CommandBuffer commands = transfer.Finish();
                device.Submit(commands).Wait();
            }

            var arguments = new ShaderArguments[batchCount];
            for (int batch = 0; batch < batchCount; batch++)
            {
                arguments[batch] = Own(pipeline.CreateArguments(device.CreateMaterialReference(materialBuffers[batch].Slice(0, materialBuffers[batch].Count))), resources);
            }

            IGraphicsTexture target = Own(device.CreateTexture(new TextureDesc { Width = Width, Height = Height }), resources);
            IGraphicsTextureView targetView = Own(target.CreateView(), resources);
            IGraphicsBuffer<byte> readback = Own(device.CreateBuffer(new BufferDesc<byte> { Count = Height * BytesPerRow, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }), resources);
            using (CommandEncoder encoder = device.CreateCommandEncoder())
            {
                using (RenderEncoder render = encoder.BeginRenderPass(new RenderPassDesc { Target = targetView, ClearValue = new Color4(0, 0, 0, 0) }))
                {
                    render.SetPipeline(pipeline);
                    for (int batch = 0; batch < batchCount; batch++)
                    {
                        // Six vertices form each quad; instance/material indices are local to the binding set.
                        render.Draw(arguments[batch], new DrawDesc { VertexCount = 6, InstanceCount = instanceCounts[batch] });
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
