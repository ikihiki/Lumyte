using System.Numerics;
using System.Reflection;
using Lumyte.Graphics;

namespace Lumyte.Samples;

// Shared with integration tests; uses only the common API.
internal static class MaterialDrawing
{
    internal static byte[] Run(GraphicsDevice device, Assembly shaders, bool swap = false, bool untextured = false)
    {
        using var red = device.CreateTexture(new TextureDesc {
            Width = 1, Height = 1, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination,
        });
        using var green = device.CreateTexture(new TextureDesc {
            Width = 1, Height = 1, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination,
        });
        using var redView = red.CreateView();
        using var greenView = green.CreateView();
        using var sampler = device.CreateSampler(new SamplerDesc { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest });
        var redReference = device.CreateSampledTexture2DReference(redView, sampler);
        var greenReference = device.CreateSampledTexture2DReference(greenView, sampler);
        using var shader = device.CreateShader(shaders, "Lumyte.Shaders.material.wgsl");
        using var pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader });
        using var bindings = device.CreateMaterialBindings(new MaterialBindingsDesc {
            Layout = shader.GetMaterialResourceLayout(), UnusedSlotFallback = redReference,
        }, new MaterialData[] {
            new(new Vector4(0.5f, 1, 1, 1), untextured ? null : swap ? greenReference : redReference),
            new(Vector4.One, swap ? redReference : greenReference),
        });
        using var upload = device.CreateBuffer(new BufferDesc<byte> {
            Count = bindings.SizeInBytes, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload,
        });
        using var gpu = device.CreateBuffer(new BufferDesc<byte> {
            Count = bindings.SizeInBytes, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead,
        });
        using var redUpload = device.CreateBuffer(new BufferDesc<byte> { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using var greenUpload = device.CreateBuffer(new BufferDesc<byte> { Count = 256, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        redUpload.CopyFrom(new byte[] { 255, 0, 0, 255 });
        greenUpload.CopyFrom(new byte[] { 0, 255, 0, 255 });
        upload.Slice(0, bindings.SizeInBytes).CopyFrom(bindings);
        // Explicit upload recording and submission. No queue writes or hidden staging.
        using (var transfer = device.CreateCommandEncoder())
        {
            transfer.RecordCopyBufferToTexture(redUpload.Slice(0, 256), red, 256);
            transfer.RecordCopyBufferToTexture(greenUpload.Slice(0, 256), green, 256);
            transfer.RecordCopyBuffer(upload.Slice(0, bindings.SizeInBytes), gpu.Slice(0, bindings.SizeInBytes));
            using var commands = transfer.Finish();
            device.Submit(commands).Wait();
        }
        using var arguments = pipeline.CreateArguments(device.CreateMaterialReference(gpu.Slice(0, bindings.SizeInBytes)));
        using var target = device.CreateTexture(new TextureDesc { Width = 8, Height = 4 });
        using var view = target.CreateView();
        using var readback = device.CreateBuffer(new BufferDesc<byte> { Count = 4 * 256, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using (var encoder = device.CreateCommandEncoder())
        {
            using (var pass = encoder.BeginRenderPass(new RenderPassDesc { Target = view }))
            {
                pass.SetPipeline(pipeline);
                pass.Draw(arguments, new DrawDesc { VertexCount = 3 });
            }
            encoder.RecordCopyTextureToBuffer(target, readback, 256);
            using var commands = encoder.Finish();
            device.Submit(commands).Wait();
        }
        var pixels = new byte[4 * 256];
        readback.CopyTo(pixels);
        return pixels;
    }
}
