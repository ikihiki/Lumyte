using System.Numerics;
using System.Reflection;
using Lumyte.Graphics;

namespace Lumyte.Samples;

// Shared with integration tests; uses only the common API.
internal static class MaterialDrawing
{
    internal static byte[] Run(GraphicsDevice device, Assembly shaders, bool swap = false, bool untextured = false)
    {
        using IGraphicsTexture red = device.CreateTexture(new TextureDesc { Width = 1, Height = 1, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination, });
        using IGraphicsTexture green = device.CreateTexture(new TextureDesc { Width = 1, Height = 1, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination, });
        using IGraphicsTextureView redView = red.CreateView();
        using IGraphicsTextureView greenView = green.CreateView();
        using Sampler sampler = device.CreateSampler(new SamplerDesc { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest });
        using IArgumentTable table = device.CreateArgumentTable(new ArgumentTableDesc { TextureCapacity = 2, SamplerCapacity = 1 });
        TextureDescriptorReference redReference = table.WriteTexture(0, redView);
        TextureDescriptorReference greenReference = table.WriteTexture(1, greenView);
        SamplerDescriptorReference samplerReference = table.WriteSampler(0, sampler);
        using ShaderModule shader = device.CreateShader(shaders, "Lumyte.Shaders.material.wgsl");
        using GraphicsPipeline pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader });
        ShaderDataLayout<MaterialData> layout = shader.GetDataLayout<MaterialData>();
        ulong byteCount = layout.GetSizeInBytes(2);
        var materials = new MaterialData[] { new(new Vector4(0.5f, 1, 1, 1), untextured ? null : swap ? greenReference : redReference, samplerReference), new(Vector4.One, swap ? redReference : greenReference, samplerReference) };
        using IGraphicsBuffer<byte> upload = device.CreateBuffer(new BufferDesc<byte> { Count = byteCount, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<byte> gpu = device.CreateBuffer(new BufferDesc<byte> { Count = byteCount, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        using IGraphicsBuffer<byte> redUpload = device.CreateBuffer(new BufferDesc<byte> { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<byte> greenUpload = device.CreateBuffer(new BufferDesc<byte> { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        redUpload.CopyFrom(new byte[] { 255, 0, 0, 255 });
        greenUpload.CopyFrom(new byte[] { 0, 255, 0, 255 });
        upload.Slice(0, byteCount).CopyFrom<MaterialData>(materials, layout, MaterialDataSerializer.Instance);

        // Explicit upload recording and submission. No queue writes or hidden staging.
        using (CommandEncoder transfer = device.CreateCommandEncoder())
        {
            transfer.RecordCopyBufferToTexture(redUpload.Slice(0, 256), red, 256);
            transfer.RecordCopyBufferToTexture(greenUpload.Slice(0, 256), green, 256);
            transfer.RecordCopyBuffer(upload.Slice(0, byteCount), gpu.Slice(0, byteCount));
            using CommandBuffer commands = transfer.Finish();
            device.Submit(commands).Wait();
        }

        using ShaderArguments arguments = pipeline.CreateArguments(device.CreateShaderDataReference<MaterialData>(gpu.Slice(0, byteCount)));
        using IGraphicsTexture target = device.CreateTexture(new TextureDesc { Width = 8, Height = 4 });
        using IGraphicsTextureView view = target.CreateView();
        using IGraphicsBuffer<byte> readback = device.CreateBuffer(new BufferDesc<byte> { Count = 4 * 256, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using (CommandEncoder encoder = device.CreateCommandEncoder())
        {
            using (RenderEncoder pass = encoder.BeginRenderPass(new RenderPassDesc { Target = view }))
            {
                pass.SetPipeline(pipeline);
                pass.Draw(arguments, new DrawDesc { VertexCount = 3 });
            }

            encoder.RecordCopyTextureToBuffer(target, readback, 256);
            using CommandBuffer commands = encoder.Finish();
            device.Submit(commands).Wait();
        }

        byte[] pixels = new byte[4 * 256];
        readback.CopyTo(pixels);
        return pixels;
    }
}
