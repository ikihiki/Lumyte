using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Lumyte.Graphics.Shader;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
public sealed class DirectX12RasterStateTests
{
    private const string TriangleVertex = """
        float4 vertexMain(uint vertexId : SV_VertexID) : SV_Position
        {
            const float2 positions[3] = {
                float2(-0.8, -0.8), float2(0.8, -0.8), float2(0.0, 0.8)
            };
            return float4(positions[vertexId], 0.0, 1.0);
        }
        """;

    private const string OrangePixel = """
        float4 pixelMain() : SV_Target0 { return float4(1.0, 0.2, 0.1, 1.0); }
        """;

    private const string AlphaPixel = """
        float4 pixelMain() : SV_Target0 { return float4(1.0, 0.0, 0.0, 0.5); }
        """;

    private const string WhitePixel = """
        float4 pixelMain() : SV_Target0 { return float4(1.0, 1.0, 1.0, 1.0); }
        """;

    private const string StripVertex = """
        float4 vertexMain(uint vertexId : SV_VertexID) : SV_Position
        {
            const float2 positions[4] = {
                float2(-0.8, -0.8), float2(-0.8, 0.8),
                float2(0.8, -0.8), float2(0.8, 0.8)
            };
            return float4(positions[vertexId], 0.0, 1.0);
        }
        """;

    private const string NearVertex = """
        float4 vertexMain(uint vertexId : SV_VertexID) : SV_Position
        {
            const float2 positions[3] = {
                float2(-0.8, -0.8), float2(0.8, -0.8), float2(0.0, 0.8)
            };
            return float4(positions[vertexId], 0.2, 1.0);
        }
        """;

    private const string FarVertex = """
        float4 vertexMain(uint vertexId : SV_VertexID) : SV_Position
        {
            const float2 positions[3] = {
                float2(-0.8, -0.8), float2(0.8, -0.8), float2(0.0, 0.8)
            };
            return float4(positions[vertexId], 0.8, 1.0);
        }
        """;

    private const string NearPixel = """
        float4 pixelMain() : SV_Target0 { return float4(0.1, 1.0, 0.2, 1.0); }
        """;

    private const string FarPixel = """
        float4 pixelMain() : SV_Target0 { return float4(1.0, 0.1, 0.2, 1.0); }
        """;

    private const string MultipleTargetPixel = """
        struct PixelOutput
        {
            float4 first : SV_Target0;
            float4 second : SV_Target1;
        };
        PixelOutput pixelMain()
        {
            PixelOutput output;
            output.first = float4(1.0, 0.0, 0.0, 1.0);
            output.second = float4(0.0, 1.0, 0.0, 1.0);
            return output;
        }
        """;

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
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
    [Trait("Category", "DirectX12Conformance")]
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
    [Trait("Category", "DirectX12Conformance")]
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
    [Trait("Category", "DirectX12Conformance")]
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
    [Trait("Category", "DirectX12Conformance")]
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
    [Trait("Category", "DirectX12Conformance")]
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

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void MultipleColorTargetsReceiveDistinctShaderOutputs()
    {
        using var fixture = new RenderFixture();
        (GpuTextureHandle secondTarget, GpuTextureView secondView) = fixture.CreateAdditionalTarget();
        GpuRasterPipelineHandle pipeline = fixture.CreatePipeline(
            new([new(GpuFormat.Rgba8Unorm), new(GpuFormat.Rgba8Unorm)]),
            TriangleVertex,
            MultipleTargetPixel);

        fixture.RenderTargets([fixture.TargetView, secondView], commands =>
            commands.SetPipeline(pipeline).Draw(3));
        byte[] firstPixels = fixture.ReadTarget(fixture.Target);
        byte[] secondPixels = fixture.ReadTarget(secondTarget);

        AssertPixelNear(firstPixels, 255, 0, 0, 255);
        AssertPixelNear(secondPixels, 0, 255, 0, 255);
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
        private readonly List<(GpuTextureHandle Texture, GpuTextureView View, GpuMemoryAllocation Memory)> additionalTargets = [];
        private readonly GpuMemoryAllocation? depthMemory;
        private readonly GpuTextureHandle? depthTexture;
        private readonly GpuTextureView? depthView;

        public RenderFixture(GpuFormat? depthFormat = null)
        {
            Device = DirectX12Device.Create();
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

        public DirectX12Device Device { get; }
        public GpuTextureHandle Target { get; }
        public GpuTextureView TargetView { get; }

        public (GpuTextureHandle Texture, GpuTextureView View) CreateAdditionalTarget()
        {
            var description = new GpuTextureDescription(
                64, 64, GpuFormat.Rgba8Unorm,
                GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);
            GpuMemoryAllocation memory = textures.AllocateMemory(description);
            GpuTextureHandle texture = textures.CreatePlacedTexture(description, memory);
            GpuTextureView view = textures.CreateView(texture, new(GpuFormat.Rgba8Unorm));
            additionalTargets.Add((texture, view, memory));
            return (texture, view);
        }

        public GpuRasterPipelineHandle CreatePipeline(
            GpuRasterPipelineDescription description,
            string vertexSource,
            string pixelSource)
        {
            byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"dx12-raster-state-{pipelines.Count}"));
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

        public void RenderTargets(
            IReadOnlyList<GpuTextureView> targetViews,
            Action<GpuCommandBuffer> draw)
        {
            GpuColorAttachment[] colors = targetViews.Select(view => new GpuColorAttachment(
                view,
                GpuAttachmentLoadOperation.Clear,
                GpuAttachmentStoreOperation.Store,
                new(0, 0, 0, 1))).ToArray();
            GpuCommandBuffer commands = Device.MainQueue.StartCommandRecording()
                .BeginRendering(colors)
                .SetViewportAndScissor(new(0, 0, 64, 64), new(0, 0, 64, 64));
            draw(commands);
            commands.EndRendering();
            Submit(commands);
        }

        public byte[] ReadTarget(GpuTextureHandle target)
        {
            GpuCommandBuffer commands = Device.MainQueue.StartCommandRecording()
                .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
                .CopyTextureToMemory(target, buffers.AddressOf(readback), new(64, 64, 4, 256));
            Submit(commands);
            return readbackMemory.MappedBytes()[..(64 * 64 * 4)].ToArray();
        }

        private void Submit(GpuCommandBuffer commands)
        {
            using GpuSemaphore completion = Device.MainQueue.CreateSemaphore();
            Device.MainQueue.Submit([commands], completion, 1);
            Device.MainQueue.Wait(completion, 1);
        }

        public void Dispose()
        {
            foreach (GpuRasterPipelineHandle pipeline in pipelines) { Device.DestroyRasterPipeline(pipeline); }
            buffers.Retire(readback, readbackMemory, new(1));
            buffers.Collect(new(1));
            foreach ((GpuTextureHandle extraTexture, GpuTextureView extraView, GpuMemoryAllocation extraMemory) in additionalTargets)
            {
                Device.DestroyTextureView(extraView);
                textures.Retire(extraTexture, extraMemory, new(1));
            }
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
            new(GpuShaderCodeFormat.Dxil, GpuShaderStage.Vertex, "vertexMain",
                "directx12", "vs_6_0", "", abiHash, Compile(vertexSource, "vertexMain", "vs_6_0")),
            new(GpuShaderCodeFormat.Dxil, GpuShaderStage.Pixel, "pixelMain",
                "directx12", "ps_6_0", "", abiHash, Compile(pixelSource, "pixelMain", "ps_6_0")),
        ]);
        return GpuShaderPackage.Read(bytes);
    }

    private static byte[] Compile(string source, string entryPoint, string profile)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lumyte-dxc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string input = Path.Combine(directory, "shader.hlsl");
        string output = Path.Combine(directory, "shader.dxil");
        try
        {
            File.WriteAllText(input, source);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(AppContext.BaseDirectory, "dxc.exe"),
                Arguments = $"-T {profile} -E {entryPoint} -Fo \"{output}\" \"{input}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }) ?? throw new InvalidOperationException("DXC could not be started.");
            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) { throw new InvalidOperationException($"DXC failed: {errors}"); }
            return File.ReadAllBytes(output);
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
        }
    }
}
