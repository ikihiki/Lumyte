using System.Security.Cryptography;
using System.Text;

using Lumyte.Graphics.Vulkan;
using Lumyte.Shaders;

using Silk.NET.Shaderc;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed class VulkanRasterStateTests
{
    private const string TriangleVertex = """
        #version 450
        const vec2 positions[3] = vec2[3](
            vec2(-0.8, -0.8), vec2(0.8, -0.8), vec2(0.0, 0.8));
        void main() { gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0); }
        """;

    private const string OrangePixel = """
        #version 450
        layout(location = 0) out vec4 color;
        void main() { color = vec4(1.0, 0.2, 0.1, 1.0); }
        """;

    private const string AlphaPixel = """
        #version 450
        layout(location = 0) out vec4 color;
        void main() { color = vec4(1.0, 0.0, 0.0, 0.5); }
        """;

    private const string WhitePixel = """
        #version 450
        layout(location = 0) out vec4 color;
        void main() { color = vec4(1.0); }
        """;

    private const string StripVertex = """
        #version 450
        const vec2 positions[4] = vec2[4](
            vec2(-0.8, -0.8), vec2(-0.8, 0.8),
            vec2(0.8, -0.8), vec2(0.8, 0.8));
        void main() { gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0); }
        """;

    private const string NearVertex = """
        #version 450
        const vec2 positions[3] = vec2[3](
            vec2(-0.8, -0.8), vec2(0.8, -0.8), vec2(0.0, 0.8));
        void main() { gl_Position = vec4(positions[gl_VertexIndex], 0.2, 1.0); }
        """;

    private const string FarVertex = """
        #version 450
        const vec2 positions[3] = vec2[3](
            vec2(-0.8, -0.8), vec2(0.8, -0.8), vec2(0.0, 0.8));
        void main() { gl_Position = vec4(positions[gl_VertexIndex], 0.8, 1.0); }
        """;

    private const string NearPixel = """
        #version 450
        layout(location = 0) out vec4 color;
        void main() { color = vec4(0.1, 1.0, 0.2, 1.0); }
        """;

    private const string FarPixel = """
        #version 450
        layout(location = 0) out vec4 color;
        void main() { color = vec4(1.0, 0.1, 0.2, 1.0); }
        """;

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void AlphaBlendCombinesSourceAndDestinationColors()
    {
        using var fixture = new RenderFixture();
        GpuRasterPipelineHandle pipeline = fixture.CreatePipeline(
            new([new(GpuFormat.Rgba8Unorm)])
            {
                EmbeddedBlend = new(
                    SourceColorFactor: GpuBlendFactor.SourceAlpha,
                    DestinationColorFactor: GpuBlendFactor.OneMinusSourceAlpha,
                    SourceAlphaFactor: GpuBlendFactor.One,
                    DestinationAlphaFactor: GpuBlendFactor.Zero),
            },
            TriangleVertex,
            AlphaPixel);

        byte[] pixels = fixture.Render(new(0, 0, 1, 1), commands =>
            commands.SetPipeline(pipeline).Draw(3));

        AssertPixelNear(pixels, 128, 0, 128, 128);
    }

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void ColorWriteMaskPreservesDisabledChannels()
    {
        using var fixture = new RenderFixture();
        GpuRasterPipelineHandle pipeline = fixture.CreatePipeline(
            new([new(GpuFormat.Rgba8Unorm, GpuColorWriteMask.Red)]),
            TriangleVertex,
            OrangePixel);

        byte[] pixels = fixture.Render(new(0, 0.4f, 0.8f, 1), commands =>
            commands.SetPipeline(pipeline).Draw(3));

        AssertPixelNear(pixels, 255, 102, 204, 255);
    }

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void TriangleStripRasterizesAQuad()
    {
        using var fixture = new RenderFixture();
        GpuRasterPipelineHandle pipeline = fixture.CreatePipeline(
            new([new(GpuFormat.Rgba8Unorm)]) { Topology = GpuPrimitiveTopology.TriangleStrip },
            StripVertex,
            WhitePixel);

        byte[] pixels = fixture.Render(default, commands =>
            commands.SetPipeline(pipeline).Draw(4));

        AssertPixelNear(pixels, 255, 255, 255, 255);
    }

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void FrontFaceSelectsWhichTriangleIsCulled()
    {
        using var fixture = new RenderFixture();
        GpuRasterPipelineHandle ccwPipeline = fixture.CreatePipeline(
            new([new(GpuFormat.Rgba8Unorm)])
            {
                CullMode = GpuCullMode.Back,
                FrontFace = GpuFrontFace.CounterClockwise,
            },
            TriangleVertex,
            OrangePixel);
        GpuRasterPipelineHandle cwPipeline = fixture.CreatePipeline(
            new([new(GpuFormat.Rgba8Unorm)])
            {
                CullMode = GpuCullMode.Back,
                FrontFace = GpuFrontFace.Clockwise,
            },
            TriangleVertex,
            OrangePixel);
        GpuRasterPipelineHandle frontCullPipeline = fixture.CreatePipeline(
            new([new(GpuFormat.Rgba8Unorm)])
            {
                CullMode = GpuCullMode.Front,
                FrontFace = GpuFrontFace.CounterClockwise,
            },
            TriangleVertex,
            OrangePixel);

        byte[] ccwPixels = fixture.Render(default, commands =>
            commands.SetPipeline(ccwPipeline).Draw(3));
        byte[] cwPixels = fixture.Render(default, commands =>
            commands.SetPipeline(cwPipeline).Draw(3));
        byte[] frontCullPixels = fixture.Render(default, commands =>
            commands.SetPipeline(frontCullPipeline).Draw(3));

        Assert.NotEqual(IsCenterColored(ccwPixels), IsCenterColored(cwPixels));
        Assert.NotEqual(IsCenterColored(ccwPixels), IsCenterColored(frontCullPixels));
    }

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void DepthTestAndWriteKeepNearestTriangleInEitherDrawOrder()
    {
        using var fixture = new RenderFixture(GpuFormat.D32Float);
        var description = new GpuRasterPipelineDescription(
            [new(GpuFormat.Rgba8Unorm)], depthFormat: GpuFormat.D32Float);
        GpuRasterPipelineHandle nearPipeline = fixture.CreatePipeline(
            description, NearVertex, NearPixel);
        GpuRasterPipelineHandle farPipeline = fixture.CreatePipeline(
            description, FarVertex, FarPixel);

        byte[] nearThenFar = fixture.Render(default, commands => commands
            .SetPipeline(nearPipeline).Draw(3)
            .SetPipeline(farPipeline).Draw(3));
        byte[] farThenNear = fixture.Render(default, commands => commands
            .SetPipeline(farPipeline).Draw(3)
            .SetPipeline(nearPipeline).Draw(3));

        AssertPixelNear(nearThenFar, 26, 255, 51, 255);
        AssertPixelNear(farThenNear, 26, 255, 51, 255);
    }

    [Fact]
    [Trait("Category", "VulkanConformance")]
    public void StencilAttachmentParticipatesInRenderedOutput()
    {
        using var fixture = new RenderFixture(GpuFormat.Depth24PlusStencil8);
        GpuRasterPipelineHandle pipeline = fixture.CreatePipeline(
            new(
                [new(GpuFormat.Rgba8Unorm)],
                stencilFormat: GpuFormat.Depth24PlusStencil8),
            TriangleVertex,
            OrangePixel);

        byte[] pixels = fixture.Render(default, commands =>
            commands.SetPipeline(pipeline).Draw(3), new(1, 7));

        AssertPixelNear(pixels, 255, 51, 26, 255);
    }

    private static bool IsCenterColored(ReadOnlySpan<byte> pixels)
        => pixels[(32 * 64 + 32) * 4] > 200;

    private static void AssertPixelNear(
        ReadOnlySpan<byte> pixels,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        int offset = (32 * 64 + 32) * 4;
        Assert.InRange(pixels[offset], Math.Max(0, red - 1), red);
        Assert.InRange(pixels[offset + 1], Math.Max(0, green - 1), Math.Min(255, green + 1));
        Assert.InRange(pixels[offset + 2], Math.Max(0, blue - 1), Math.Min(255, blue + 1));
        Assert.InRange(pixels[offset + 3], Math.Max(0, alpha - 1), alpha);
    }

    private sealed class RenderFixture : IDisposable
    {
        private readonly GpuPersistentArena textureArena;
        private readonly GpuPersistentArena bufferArena;
        private readonly GpuManualTextureAllocator textures;
        private readonly GpuManualBufferAllocator buffers;
        private readonly GpuMemoryAllocation targetMemory;
        private readonly GpuMemoryAllocation readbackMemory;
        private readonly GpuBufferHandle readback;
        private readonly List<GpuRasterPipelineHandle> pipelines = [];
        private readonly GpuMemoryAllocation? depthMemory;
        private readonly GpuTextureHandle? depthTexture;
        private readonly GpuTextureView? depthView;

        public RenderFixture(GpuFormat? depthFormat = null)
        {
            Device = VulkanDevice.Create();
            textureArena = new(Device);
            bufferArena = new(Device);
            textures = new(Device, textureArena);
            buffers = new(Device, bufferArena);
            var targetDescription = new GpuTextureDescription(
                64, 64, GpuFormat.Rgba8Unorm,
                GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);
            targetMemory = textures.AllocateMemory(targetDescription);
            Target = textures.CreatePlacedTexture(targetDescription, targetMemory);
            TargetView = textures.CreateView(Target, new(GpuFormat.Rgba8Unorm));
            var readbackDescription = new GpuBufferDescription(64 * 64 * 4, GpuBufferUsage.CopyDestination);
            readbackMemory = buffers.AllocateMemory(readbackDescription, GpuMemoryKind.HostCached);
            readback = buffers.CreatePlacedBuffer(readbackDescription, readbackMemory);

            if (depthFormat is { } format)
            {
                var depthDescription = new GpuTextureDescription(
                    64, 64, format, GpuTextureUsage.DepthStencilAttachment);
                depthMemory = textures.AllocateMemory(depthDescription);
                depthTexture = textures.CreatePlacedTexture(depthDescription, depthMemory.Value);
                depthView = textures.CreateView(depthTexture.Value, new(format));
            }
        }

        public VulkanDevice Device { get; }
        public GpuTextureHandle Target { get; }
        public GpuTextureView TargetView { get; }

        public GpuRasterPipelineHandle CreatePipeline(
            GpuRasterPipelineDescription description,
            string vertexSource,
            string pixelSource)
        {
            byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"vulkan-raster-state-{pipelines.Count}"));
            GpuShaderPackage package = CreatePackage(vertexSource, pixelSource, abiHash);
            GpuRasterPipelineHandle pipeline = Device.CreateRasterPipeline(
                description, package, "vertexMain", "pixelMain", abiHash);
            pipelines.Add(pipeline);
            return pipeline;
        }

        public byte[] Render(
            GpuClearColor clear,
            Action<GpuCommandBuffer> draw,
            GpuClearDepthStencil depthClear = default)
        {
            var color = new GpuColorAttachment(
                TargetView,
                GpuAttachmentLoadOperation.Clear,
                GpuAttachmentStoreOperation.Store,
                clear == default ? new(0, 0, 0, 1) : clear);
            GpuDepthStencilAttachment? depth = depthView is { } view
                ? new(view, GpuAttachmentLoadOperation.Clear, GpuAttachmentStoreOperation.Store,
                    depthClear == default ? new(1, 0) : depthClear)
                : null;
            GpuCommandBuffer commands = Device.MainQueue.StartCommandRecording()
                .BeginRendering([color], depth)
                .SetViewportAndScissor(new(0, 0, 64, 64), new(0, 0, 64, 64));
            draw(commands);
            commands.EndRendering()
                .Barrier(GpuStage.ColorOutput | GpuStage.DepthStencil, GpuStage.Copy)
                .CopyTextureToMemory(Target, buffers.AddressOf(readback), new(64, 64, 4, 256));
            using GpuSemaphore completion = Device.MainQueue.CreateSemaphore();
            Device.MainQueue.Submit([commands], completion, 1);
            Device.MainQueue.Wait(completion, 1);
            return readbackMemory.MappedBytes()[..(64 * 64 * 4)].ToArray();
        }

        public void Dispose()
        {
            foreach (GpuRasterPipelineHandle pipeline in pipelines) { Device.DestroyRasterPipeline(pipeline); }
            buffers.Retire(readback, readbackMemory, new(1));
            buffers.Collect(new(1));
            if (depthView is { } view) { Device.DestroyTextureView(view); }
            Device.DestroyTextureView(TargetView);
            if (depthTexture is { } texture && depthMemory is { } memory)
            {
                textures.Retire(texture, memory, new(1));
            }
            textures.Retire(Target, targetMemory, new(1));
            textures.Collect(new(1));
            textures.VerifyEmpty();
            Device.Dispose();
        }
    }

    private static GpuShaderPackage CreatePackage(
        string vertexSource,
        string pixelSource,
        byte[] abiHash)
    {
        byte[] bytes = GpuShaderPackageWriter.Write([
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Vertex, "vertexMain",
                "vulkan", "spirv1.3", "", abiHash,
                TriangleShaders.Compile(vertexSource, ShaderKind.VertexShader)),
            new(GpuShaderCodeFormat.SpirV, GpuShaderStage.Pixel, "pixelMain",
                "vulkan", "spirv1.3", "", abiHash,
                TriangleShaders.Compile(pixelSource, ShaderKind.FragmentShader)),
        ]);
        return GpuShaderPackage.Read(bytes);
    }
}
