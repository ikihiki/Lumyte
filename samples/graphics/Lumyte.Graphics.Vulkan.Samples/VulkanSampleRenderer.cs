using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Library;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Shader;

using Silk.NET.Shaderc;

namespace Lumyte.Graphics.Vulkan.Samples;

internal sealed class VulkanSampleRenderer : IDisposable
{
    private readonly VulkanDevice device;
    private readonly GpuFormat colorFormat;
    private GpuRasterPipelineHandle trianglePipeline;
    private GpuRasterPipelineHandle texturePipeline;
    private GpuRasterPipelineHandle scenePipeline;
    private GpuRasterPipelineHandle lightPipeline;
    private GpuRasterPipelineHandle materialPipeline;
    private GeneratedTexture? generatedTexture;
    private MaterialDrawSample? materialDrawSample;
    private bool disposed;

    internal VulkanSampleRenderer(VulkanDevice device, GpuFormat colorFormat)
    {
        this.device = device ?? throw new ArgumentNullException(nameof(device));
        this.colorFormat = colorFormat;
        try
        {
            GpuShaderPackage shaders = CreateShaderPackage();
            byte[] abiHash = ShaderAbiHash();
            var opaqueDescription = new GpuRasterPipelineDescription([new(colorFormat)]);
            trianglePipeline = device.CreateRasterPipeline(
                opaqueDescription,
                shaders,
                "triangleVertex",
                "trianglePixel",
                abiHash);
            texturePipeline = device.CreateRasterPipeline(
                opaqueDescription,
                shaders,
                "quadVertex",
                "texturedPixel",
                abiHash);
            scenePipeline = device.CreateRasterPipeline(
                opaqueDescription,
                shaders,
                "fullscreenVertex",
                "scenePixel",
                abiHash);
            lightPipeline = device.CreateRasterPipeline(
                new GpuRasterPipelineDescription([new(colorFormat)])
                {
                    EmbeddedBlend = new(
                        SourceColorFactor: GpuBlendFactor.One,
                        DestinationColorFactor: GpuBlendFactor.One,
                        SourceAlphaFactor: GpuBlendFactor.Zero,
                        DestinationAlphaFactor: GpuBlendFactor.One),
                },
                shaders,
                "fullscreenVertex",
                "lightPixel",
                abiHash);
            materialPipeline = device.CreateRasterPipeline(
                opaqueDescription,
                shaders,
                "materialVertex",
                "materialPixel",
                abiHash);
            generatedTexture = GeneratedTexture.Create(device);
            materialDrawSample = MaterialDrawSample.Create(
                device,
                materialPipeline,
                generatedTexture.Texture,
                generatedTexture.Description,
                generatedTexture.View.Id,
                generatedTexture.Sampler);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal GpuCommandBuffer Record(
        SampleKind sample,
        VulkanPresentationFrame frame,
        uint width,
        uint height,
        float elapsedSeconds)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return sample switch
        {
            SampleKind.Clear => RecordClear(frame),
            SampleKind.RainbowTriangle => RecordTriangle(frame, width, height),
            SampleKind.GeneratedTexture => RecordTexture(frame, width, height),
            SampleKind.RenderGraphLighting => RecordLighting(
                frame,
                width,
                height,
                elapsedSeconds),
            SampleKind.AddDrawMaterial => RecordMaterial(frame, width, height),
            _ => throw new ArgumentOutOfRangeException(nameof(sample)),
        };
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        materialDrawSample?.Dispose();
        materialDrawSample = null;
        generatedTexture?.Dispose();
        generatedTexture = null;
        DestroyPipeline(ref lightPipeline);
        DestroyPipeline(ref scenePipeline);
        DestroyPipeline(ref materialPipeline);
        DestroyPipeline(ref texturePipeline);
        DestroyPipeline(ref trianglePipeline);
    }

    private GpuCommandBuffer RecordClear(VulkanPresentationFrame frame)
        => device.MainQueue.StartCommandRecording()
            .Barrier(GpuStage.None, GpuStage.ColorOutput)
            .BeginRendering([
                new(
                    frame.View,
                    GpuAttachmentLoadOperation.Clear,
                    GpuAttachmentStoreOperation.Store,
                    new(0.018f, 0.028f, 0.055f, 1)),
            ])
            .EndRendering();

    private GpuCommandBuffer RecordTriangle(
        VulkanPresentationFrame frame,
        uint width,
        uint height)
        => device.MainQueue.StartCommandRecording()
            .Barrier(GpuStage.None, GpuStage.ColorOutput)
            .BeginRendering([
                new(
                    frame.View,
                    GpuAttachmentLoadOperation.Clear,
                    GpuAttachmentStoreOperation.Store,
                    new(0.018f, 0.028f, 0.055f, 1)),
            ])
            .SetPipeline(trianglePipeline)
            .SetViewportAndScissor(
                new(0, 0, width, height),
                new(0, 0, width, height))
            .Draw(3)
            .EndRendering();

    private GpuCommandBuffer RecordTexture(
        VulkanPresentationFrame frame,
        uint width,
        uint height)
    {
        GeneratedTexture texture = generatedTexture
            ?? throw new InvalidOperationException("Generated texture is unavailable.");
        return device.MainQueue.StartCommandRecording()
            .Barrier(GpuStage.None, GpuStage.ColorOutput)
            .BeginRendering([
                new(
                    frame.View,
                    GpuAttachmentLoadOperation.Clear,
                    GpuAttachmentStoreOperation.Store,
                    new(0.018f, 0.028f, 0.055f, 1)),
            ])
            .SetPipeline(texturePipeline)
            .SetResourceTable(texture.ResourceTable)
            .SetRootData([
                .. BitConverter.GetBytes(0u),
                .. BitConverter.GetBytes(0u),
            ])
            .SetViewportAndScissor(
                new(0, 0, width, height),
                new(0, 0, width, height))
            .Draw(6)
            .EndRendering();
    }

    private GpuCommandBuffer RecordLighting(
        VulkanPresentationFrame frame,
        uint width,
        uint height,
        float elapsedSeconds)
    {
        byte[] lightingRootData =
        [
            .. BitConverter.GetBytes(elapsedSeconds),
            .. BitConverter.GetBytes((float)width / height),
            .. BitConverter.GetBytes(0f),
            .. BitConverter.GetBytes(0f),
        ];
        var backBufferDescription = new GpuTextureDescription(
            width,
            height,
            colorFormat,
            GpuTextureUsage.ColorAttachment);
        var graph = new GpuRenderGraph();
        GpuRenderGraphTexture backBuffer = graph.ImportTexture(
            "swapchain-back-buffer",
            frame.View.Texture,
            backBufferDescription);
        graph.AddPass(
                "scene",
                (View: frame.View, Pipeline: scenePipeline, RootData: lightingRootData, Width: width, Height: height),
                static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        state.View,
                        GpuAttachmentLoadOperation.Clear,
                        GpuAttachmentStoreOperation.Store,
                        new(0.01f, 0.02f, 0.05f, 1)),
                ])
                .SetPipeline(state.Pipeline)
                .SetRootData(state.RootData)
                .SetViewportAndScissor(
                    new(0, 0, state.Width, state.Height),
                    new(0, 0, state.Width, state.Height))
                .Draw(6)
                .EndRendering())
            .Write(backBuffer, GpuStage.ColorOutput);
        graph.AddPass(
                "additive-light",
                (View: frame.View, Pipeline: lightPipeline, RootData: lightingRootData, Width: width, Height: height),
                static (context, state) => context.Commands
                .BeginRendering([
                    new(
                        state.View,
                        GpuAttachmentLoadOperation.Load,
                        GpuAttachmentStoreOperation.Store),
                ])
                .SetPipeline(state.Pipeline)
                .SetRootData(state.RootData)
                .SetViewportAndScissor(
                    new(0, 0, state.Width, state.Height),
                    new(0, 0, state.Width, state.Height))
                .Draw(6)
                .EndRendering())
            .ReadWrite(backBuffer, GpuStage.ColorOutput);
        graph.MarkOutput(backBuffer);
        return graph.Compile().Record(device.MainQueue);
    }

    private GpuCommandBuffer RecordMaterial(
        VulkanPresentationFrame frame,
        uint width,
        uint height)
    {
        MaterialDrawSample sample = materialDrawSample
            ?? throw new InvalidOperationException("Material draw sample is unavailable.");
        var targetDescription = new GpuTextureDescription(
            width,
            height,
            colorFormat,
            GpuTextureUsage.ColorAttachment);
        var target = new DrawRenderTarget(
            frame.View,
            targetDescription,
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store,
            new(0.018f, 0.028f, 0.055f, 1));
        var graph = new GpuRenderGraph();
        graph.AddDraw("material-quad", sample.Draw, target);
        return graph.Compile().Record(device.MainQueue);
    }

    private void DestroyPipeline(ref GpuRasterPipelineHandle pipeline)
    {
        if (pipeline.IsNull)
        {
            return;
        }

        device.DestroyRasterPipeline(pipeline);
        pipeline = default;
    }

    private static GpuShaderPackage CreateShaderPackage()
    {
        byte[] abiHash = ShaderAbiHash();
        return GpuShaderPackage.Read(GpuShaderPackageWriter.Write([
            Shader("triangleVertex", GpuShaderStage.Vertex, SampleShaders.TriangleVertex, ShaderKind.VertexShader),
            Shader("trianglePixel", GpuShaderStage.Pixel, SampleShaders.TrianglePixel, ShaderKind.FragmentShader),
            Shader("quadVertex", GpuShaderStage.Vertex, SampleShaders.QuadVertex, ShaderKind.VertexShader),
            Shader("texturedPixel", GpuShaderStage.Pixel, SampleShaders.TexturedPixel, ShaderKind.FragmentShader),
            Shader("fullscreenVertex", GpuShaderStage.Vertex, SampleShaders.FullscreenVertex, ShaderKind.VertexShader),
            Shader("scenePixel", GpuShaderStage.Pixel, SampleShaders.ScenePixel, ShaderKind.FragmentShader),
            Shader("lightPixel", GpuShaderStage.Pixel, SampleShaders.LightPixel, ShaderKind.FragmentShader),
            Shader("materialVertex", GpuShaderStage.Vertex, SampleShaders.MaterialVertex, ShaderKind.VertexShader),
            Shader("materialPixel", GpuShaderStage.Pixel, SampleShaders.MaterialPixel, ShaderKind.FragmentShader),
        ]));

        GpuShaderArtifactSource Shader(
            string entryPoint,
            GpuShaderStage stage,
            string source,
            ShaderKind kind)
            => new(
                GpuShaderCodeFormat.SpirV,
                stage,
                entryPoint,
                "vulkan",
                "spirv1.3",
                "",
                abiHash,
                SampleShaderCompiler.Compile(source, kind));
    }

    private static byte[] ShaderAbiHash()
        => GpuShaderBindingConvention.AbiHash.ToArray();

    private sealed class GeneratedTexture : IDisposable
    {
        private const uint Width = 256;
        private const uint Height = 256;
        private readonly VulkanDevice device;
        private GpuMemoryAllocation memory;
        private GpuTextureHandle texture;
        private GpuTextureView view;
        private SamplerId sampler;
        private bool disposed;

        private GeneratedTexture(
            VulkanDevice device,
            GpuMemoryAllocation memory,
            GpuTextureHandle texture,
            GpuTextureView view,
            SamplerId sampler,
            GpuResourceTable resourceTable)
        {
            this.device = device;
            this.memory = memory;
            this.texture = texture;
            this.view = view;
            this.sampler = sampler;
            ResourceTable = resourceTable;
        }

        internal GpuResourceTable ResourceTable { get; }
        internal GpuTextureHandle Texture => texture;
        internal GpuTextureView View => view;
        internal SamplerId Sampler => sampler;
        internal GpuTextureDescription Description => new(
            Width,
            Height,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination);

        internal static GeneratedTexture Create(VulkanDevice device)
        {
            var description = new GpuTextureDescription(
                Width,
                Height,
                GpuFormat.Rgba8Unorm,
                GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination);
            GpuTextureMemoryRequirements requirements =
                device.GetTextureMemoryRequirements(description);
            GpuMemoryAllocation memory = device.AllocateMemory(
                requirements.Size,
                requirements.Alignment,
                GpuMemoryKind.DeviceLocal);
            GpuTextureHandle texture = default;
            GpuTextureView view = default;
            SamplerId sampler = default;
            try
            {
                texture = device.CreatePlacedTexture(description, memory);
                view = device.CreateTextureView(texture, new(GpuFormat.Rgba8Unorm));
                sampler = device.CreateSampler(new(
                    GpuSamplerFilter.Linear,
                    GpuSamplerFilter.Linear,
                    GpuSamplerAddressMode.ClampToEdge,
                    GpuSamplerAddressMode.ClampToEdge));
                Upload(device, texture, CreatePixels());
                var resources = new GpuResourceTable(1, 1);
                resources.SetTexture(0, view.Id);
                resources.SetSampler(0, sampler);
                return new(device, memory, texture, view, sampler, resources);
            }
            catch
            {
                if (!sampler.IsNull) { device.DestroySampler(sampler); }
                if (!view.Id.IsNull) { device.DestroyTextureView(view); }
                if (!texture.IsNull) { device.DestroyTexture(texture); }
                device.FreeMemory(memory);
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            device.DestroySampler(sampler);
            device.DestroyTextureView(view);
            device.DestroyTexture(texture);
            device.FreeMemory(memory);
            sampler = default;
            view = default;
            texture = default;
            memory = default;
        }

        internal static byte[] CreatePixels()
        {
            byte[] pixels = new byte[Width * Height * 4];
            for (uint y = 0; y < Height; y++)
            {
                for (uint x = 0; x < Width; x++)
                {
                    bool alternate = ((x / 32) + (y / 32)) % 2 != 0;
                    float horizontal = (float)x / (Width - 1);
                    float vertical = (float)y / (Height - 1);
                    int offset = checked((int)((y * Width + x) * 4));
                    pixels[offset] = ToByte(
                        (alternate ? 0.18f : 0.95f) * (0.65f + horizontal * 0.35f));
                    pixels[offset + 1] = ToByte(
                        (alternate ? 0.78f : 0.24f) * (0.7f + vertical * 0.3f));
                    pixels[offset + 2] = ToByte(alternate ? 0.94f : 0.46f);
                    pixels[offset + 3] = 255;
                }
            }

            return pixels;
        }

        private static void Upload(
            VulkanDevice device,
            GpuTextureHandle texture,
            byte[] pixels)
        {
            var description = new GpuBufferDescription(
                (ulong)pixels.Length,
                GpuBufferUsage.CopySource);
            GpuBufferMemoryRequirements requirements =
                device.GetBufferMemoryRequirements(description);
            GpuMemoryAllocation memory = device.AllocateMemory(
                requirements.Size,
                requirements.Alignment,
                GpuMemoryKind.HostMapped);
            GpuBufferHandle buffer = default;
            try
            {
                buffer = device.CreatePlacedBuffer(description, memory);
                pixels.CopyTo(memory.MappedBytes());
                GpuMemoryAddress address = device.GetBufferMemoryAddress(
                    buffer,
                    0,
                    (ulong)pixels.Length);
                GpuCommandBuffer commands = device.MainQueue.StartCommandRecording()
                    .CopyMemoryToTexture(
                        address,
                        texture,
                        new(Width, Height, 4, Width * 4));
                using GpuSemaphore completion = device.MainQueue.CreateSemaphore();
                device.MainQueue.Submit([commands], completion, 1);
                device.MainQueue.Wait(completion, 1);
            }
            finally
            {
                if (!buffer.IsNull) { device.DestroyBuffer(buffer); }
                device.FreeMemory(memory);
            }
        }

        private static byte ToByte(float value)
            => checked((byte)MathF.Round(Math.Clamp(value, 0, 1) * byte.MaxValue));
    }
}
