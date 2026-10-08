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
        SampledTexture2DReference redReference = device.CreateSampledTexture2DReference(redView, sampler);
        SampledTexture2DReference greenReference = device.CreateSampledTexture2DReference(greenView, sampler);
        using ShaderModule shader = device.CreateShader(shaders, "Lumyte.Shaders.material.wgsl");
        using GraphicsPipeline pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader });
        using IGraphicsMaterialBindings bindings = device.CreateMaterialBindings(new MaterialBindingsDesc { Layout = shader.GetMaterialResourceLayout(), UnusedSlotFallback = redReference, }, new MaterialData[] { new(new Vector4(0.5f, 1, 1, 1), untextured ? null : swap ? greenReference : redReference), new(Vector4.One, swap ? redReference : greenReference), });
        using IGraphicsBuffer<byte> upload = device.CreateBuffer(new BufferDesc<byte> { Count = bindings.SizeInBytes, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload, });
        using IGraphicsBuffer<byte> gpu = device.CreateBuffer(new BufferDesc<byte> { Count = bindings.SizeInBytes, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead, });
        using IGraphicsBuffer<byte> redUpload = device.CreateBuffer(new BufferDesc<byte> { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<byte> greenUpload = device.CreateBuffer(new BufferDesc<byte> { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        redUpload.CopyFrom(new byte[] { 255, 0, 0, 255 });
        greenUpload.CopyFrom(new byte[] { 0, 255, 0, 255 });
        upload.Slice(0, bindings.SizeInBytes).CopyFrom(bindings);

        // Explicit upload recording and submission. No queue writes or hidden staging.
        using (CommandEncoder transfer = device.CreateCommandEncoder())
        {
            transfer.RecordCopyBufferToTexture(redUpload.Slice(0, 256), red, 256);
            transfer.RecordCopyBufferToTexture(greenUpload.Slice(0, 256), green, 256);
            transfer.RecordCopyBuffer(upload.Slice(0, bindings.SizeInBytes), gpu.Slice(0, bindings.SizeInBytes));
            using CommandBuffer commands = transfer.Finish();
            device.Submit(commands).Wait();
        }

        using ShaderArguments arguments = pipeline.CreateArguments(device.CreateMaterialReference(gpu.Slice(0, bindings.SizeInBytes)));
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
