using System.Security.Cryptography;
using System.Text;

using Lumyte.Shaders;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
public sealed class WebGpuRenderingTests
{
    private const uint TargetWidth = 64;
    private const uint TargetHeight = 64;

    private const string TriangleShader = """
        @vertex
        fn triangleVertex(@builtin(vertex_index) vertexIndex: u32) -> @builtin(position) vec4<f32> {
            var positions = array<vec2<f32>, 3>(
                vec2<f32>(-0.8, -0.8),
                vec2<f32>( 0.8, -0.8),
                vec2<f32>( 0.0,  0.8)
            );
            return vec4<f32>(positions[vertexIndex], 0.0, 1.0);
        }

        @fragment
        fn triangleFragment() -> @location(0) vec4<f32> {
            return vec4<f32>(1.0, 0.2, 0.1, 1.0);
        }
        """;

    private const string DepthShader = """
        fn trianglePosition(vertexIndex: u32, depth: f32) -> vec4<f32> {
            var positions = array<vec2<f32>, 3>(
                vec2<f32>(-0.8, -0.8),
                vec2<f32>( 0.8, -0.8),
                vec2<f32>( 0.0,  0.8)
            );
            return vec4<f32>(positions[vertexIndex], depth, 1.0);
        }

        @vertex
        fn nearVertex(@builtin(vertex_index) vertexIndex: u32) -> @builtin(position) vec4<f32> {
            return trianglePosition(vertexIndex, 0.2);
        }

        @fragment
        fn nearFragment() -> @location(0) vec4<f32> {
            return vec4<f32>(0.1, 1.0, 0.2, 1.0);
        }

        @vertex
        fn farVertex(@builtin(vertex_index) vertexIndex: u32) -> @builtin(position) vec4<f32> {
            return trianglePosition(vertexIndex, 0.8);
        }

        @fragment
        fn farFragment() -> @location(0) vec4<f32> {
            return vec4<f32>(1.0, 0.1, 0.2, 1.0);
        }
        """;

    private const string AlphaShader = """
        @vertex
        fn alphaVertex(@builtin(vertex_index) vertexIndex: u32) -> @builtin(position) vec4<f32> {
            var positions = array<vec2<f32>, 3>(
                vec2<f32>(-0.8, -0.8),
                vec2<f32>( 0.8, -0.8),
                vec2<f32>( 0.0,  0.8)
            );
            return vec4<f32>(positions[vertexIndex], 0.0, 1.0);
        }

        @fragment
        fn alphaFragment() -> @location(0) vec4<f32> {
            return vec4<f32>(1.0, 0.0, 0.0, 0.5);
        }
        """;

    private const string StripShader = """
        @vertex
        fn stripVertex(@builtin(vertex_index) vertexIndex: u32) -> @builtin(position) vec4<f32> {
            var positions = array<vec2<f32>, 4>(
                vec2<f32>(-0.8, -0.8),
                vec2<f32>(-0.8,  0.8),
                vec2<f32>( 0.8, -0.8),
                vec2<f32>( 0.8,  0.8)
            );
            return vec4<f32>(positions[vertexIndex], 0.0, 1.0);
        }

        @fragment
        fn stripFragment() -> @location(0) vec4<f32> {
            return vec4<f32>(1.0, 1.0, 1.0, 1.0);
        }
        """;

    private const string TexturedQuadShader = """
        struct VertexOutput {
            @builtin(position) position: vec4<f32>,
            @location(0) uv: vec2<f32>,
        }

        @vertex
        fn quadVertex(@builtin(vertex_index) vertexIndex: u32) -> VertexOutput {
            var positions = array<vec2<f32>, 6>(
                vec2<f32>(-1.0, -1.0), vec2<f32>( 1.0, -1.0), vec2<f32>( 1.0,  1.0),
                vec2<f32>(-1.0, -1.0), vec2<f32>( 1.0,  1.0), vec2<f32>(-1.0,  1.0)
            );
            var uvs = array<vec2<f32>, 6>(
                vec2<f32>(0.0, 1.0), vec2<f32>(1.0, 1.0), vec2<f32>(1.0, 0.0),
                vec2<f32>(0.0, 1.0), vec2<f32>(1.0, 0.0), vec2<f32>(0.0, 0.0)
            );
            var output: VertexOutput;
            output.position = vec4<f32>(positions[vertexIndex], 0.0, 1.0);
            output.uv = uvs[vertexIndex];
            return output;
        }

        @group(0) @binding(0) var sourceTexture: texture_2d<f32>;
        @group(0) @binding(64) var sourceSampler: sampler;

        @fragment
        fn quadFragment(input: VertexOutput) -> @location(0) vec4<f32> {
            return textureSample(sourceTexture, sourceSampler, input.uv);
        }
        """;

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void RasterizedTriangleCanBeReadBack()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("webgpu-triangle"));
        GpuShaderPackage package = CreatePackage(
            TriangleShader, "triangleVertex", "triangleFragment", abiHash);
        GpuTextureHandle target = backend.CreateTexture(new(
            TargetWidth,
            TargetHeight,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource));
        GpuTextureView targetView = backend.CreateTextureView(target, new(GpuFormat.Rgba8Unorm));
        GpuRasterPipelineHandle pipeline = backend.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]),
            package,
            "triangleVertex",
            "triangleFragment",
            abiHash);

        Draw(backend, targetView, pipeline, null, 3);
        byte[] pixels = backend.ReadTexture(
            target, new(TargetWidth, TargetHeight, 4, TargetWidth * 4));

        AssertPixelNear(pixels, 32, 32, 255, 51, 26, 255);
        AssertPixel(pixels, 0, 0, 0, 0, 0, 255);

        backend.DestroyRasterPipeline(pipeline);
        backend.DestroyTextureView(targetView);
        backend.DestroyTexture(target);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void UploadedTextureCanBeSampledByAQuad()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("webgpu-textured-quad"));
        GpuShaderPackage package = CreatePackage(
            TexturedQuadShader, "quadVertex", "quadFragment", abiHash);
        byte[] texels =
        [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255,
        ];
        GpuTextureHandle texture = backend.CreateTexture(new(
            2, 2, GpuFormat.Rgba8Unorm,
            GpuTextureUsage.CopyDestination | GpuTextureUsage.Sampled));
        GpuTextureView view = backend.CreateTextureView(texture, new(GpuFormat.Rgba8Unorm));
        SamplerId sampler = backend.CreateSampler(default);
        var resources = new GpuResourceTable(1, 1);
        resources.SetTexture(0, view.Id);
        resources.SetSampler(0, sampler);
        backend.WriteTexture(texture, texels, new(2, 2, 4, 8));
        GpuTextureHandle target = backend.CreateTexture(new(
            TargetWidth,
            TargetHeight,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource));
        GpuTextureView targetView = backend.CreateTextureView(target, new(GpuFormat.Rgba8Unorm));
        GpuRasterPipelineHandle pipeline = backend.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]),
            package,
            "quadVertex",
            "quadFragment",
            abiHash);

        Draw(backend, targetView, pipeline, resources, 6);
        byte[] pixels = backend.ReadTexture(
            target, new(TargetWidth, TargetHeight, 4, TargetWidth * 4));

        AssertPixel(pixels, 16, 16, 255, 0, 0, 255);
        AssertPixel(pixels, 48, 16, 0, 255, 0, 255);
        AssertPixel(pixels, 16, 48, 0, 0, 255, 255);
        AssertPixel(pixels, 48, 48, 255, 255, 255, 255);

        backend.DestroyRasterPipeline(pipeline);
        backend.DestroySampler(sampler);
        backend.DestroyTextureView(view);
        backend.DestroyTextureView(targetView);
        backend.DestroyTexture(texture);
        backend.DestroyTexture(target);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void DepthTestAndWriteKeepNearestTriangleInEitherDrawOrder()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("webgpu-depth"));
        GpuShaderPackage nearPackage = CreatePackage(DepthShader, "nearVertex", "nearFragment", abiHash);
        GpuShaderPackage farPackage = CreatePackage(DepthShader, "farVertex", "farFragment", abiHash);
        GpuTextureHandle target = backend.CreateTexture(new(
            TargetWidth, TargetHeight, GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource));
        GpuTextureView targetView = backend.CreateTextureView(target, new(GpuFormat.Rgba8Unorm));
        GpuTextureHandle depth = backend.CreateTexture(new(
            TargetWidth, TargetHeight, GpuFormat.D32Float, GpuTextureUsage.DepthStencilAttachment));
        GpuTextureView depthView = backend.CreateTextureView(depth, new(GpuFormat.D32Float));
        var description = new GpuRasterPipelineDescription(
            [new(GpuFormat.Rgba8Unorm)], depthFormat: GpuFormat.D32Float);
        GpuRasterPipelineHandle nearPipeline = backend.CreateRasterPipeline(
            description, nearPackage, "nearVertex", "nearFragment", abiHash);
        GpuRasterPipelineHandle farPipeline = backend.CreateRasterPipeline(
            description, farPackage, "farVertex", "farFragment", abiHash);
        var depthAttachment = new GpuDepthStencilAttachment(
            depthView,
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store,
            new(1, 0));

        GpuCommandBuffer nearThenFarCommands = BeginDraw(backend, targetView, depthAttachment)
            .SetPipeline(nearPipeline)
            .Draw(3)
            .SetPipeline(farPipeline)
            .Draw(3)
            .EndRendering();
        Submit(backend, nearThenFarCommands);
        byte[] nearThenFar = backend.ReadTexture(
            target, new(TargetWidth, TargetHeight, 4, TargetWidth * 4));
        GpuCommandBuffer farThenNearCommands = BeginDraw(backend, targetView, depthAttachment)
            .SetPipeline(farPipeline)
            .Draw(3)
            .SetPipeline(nearPipeline)
            .Draw(3)
            .EndRendering();
        Submit(backend, farThenNearCommands);
        byte[] farThenNear = backend.ReadTexture(
            target, new(TargetWidth, TargetHeight, 4, TargetWidth * 4));

        AssertPixelNear(nearThenFar, 32, 32, 26, 255, 51, 255);
        AssertPixelNear(farThenNear, 32, 32, 26, 255, 51, 255);

        backend.DestroyRasterPipeline(farPipeline);
        backend.DestroyRasterPipeline(nearPipeline);
        backend.DestroyTextureView(depthView);
        backend.DestroyTextureView(targetView);
        backend.DestroyTexture(depth);
        backend.DestroyTexture(target);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void StencilAttachmentParticipatesInRenderedOutput()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("webgpu-stencil"));
        GpuShaderPackage package = CreatePackage(
            TriangleShader, "triangleVertex", "triangleFragment", abiHash);
        GpuTextureHandle target = backend.CreateTexture(new(
            TargetWidth, TargetHeight, GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource));
        GpuTextureView targetView = backend.CreateTextureView(target, new(GpuFormat.Rgba8Unorm));
        GpuTextureHandle stencil = backend.CreateTexture(new(
            TargetWidth,
            TargetHeight,
            GpuFormat.Depth24PlusStencil8,
            GpuTextureUsage.DepthStencilAttachment));
        GpuTextureView stencilView = backend.CreateTextureView(
            stencil, new(GpuFormat.Depth24PlusStencil8));
        GpuRasterPipelineHandle pipeline = backend.CreateRasterPipeline(
            new GpuRasterPipelineDescription(
                [new(GpuFormat.Rgba8Unorm)],
                stencilFormat: GpuFormat.Depth24PlusStencil8),
            package,
            "triangleVertex",
            "triangleFragment",
            abiHash);
        var stencilAttachment = new GpuDepthStencilAttachment(
            stencilView,
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store,
            new(1, 7));

        GpuCommandBuffer commands = BeginDraw(backend, targetView, stencilAttachment)
            .SetPipeline(pipeline)
            .Draw(3)
            .EndRendering();
        Submit(backend, commands);
        byte[] pixels = backend.ReadTexture(
            target, new(TargetWidth, TargetHeight, 4, TargetWidth * 4));

        AssertPixelNear(pixels, 32, 32, 255, 51, 26, 255);

        backend.DestroyRasterPipeline(pipeline);
        backend.DestroyTextureView(stencilView);
        backend.DestroyTextureView(targetView);
        backend.DestroyTexture(stencil);
        backend.DestroyTexture(target);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void AlphaBlendCombinesSourceAndDestinationColors()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("webgpu-alpha-blend"));
        GpuShaderPackage package = CreatePackage(AlphaShader, "alphaVertex", "alphaFragment", abiHash);
        GpuTextureHandle target = CreateTarget(backend);
        GpuTextureView targetView = backend.CreateTextureView(target, new(GpuFormat.Rgba8Unorm));
        GpuRasterPipelineHandle pipeline = backend.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)])
            {
                EmbeddedBlend = new(
                    SourceColorFactor: GpuBlendFactor.SourceAlpha,
                    DestinationColorFactor: GpuBlendFactor.OneMinusSourceAlpha,
                    SourceAlphaFactor: GpuBlendFactor.One,
                    DestinationAlphaFactor: GpuBlendFactor.Zero),
            },
            package,
            "alphaVertex",
            "alphaFragment",
            abiHash);

        GpuCommandBuffer commands = BeginDraw(
                backend, targetView, null, new(0, 0, 1, 1))
            .SetPipeline(pipeline)
            .Draw(3)
            .EndRendering();
        Submit(backend, commands);
        byte[] pixels = ReadTarget(backend, target);

        AssertPixelNear(pixels, 32, 32, 128, 0, 128, 128);

        DestroyTarget(backend, target, targetView, pipeline);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void ColorWriteMaskPreservesDisabledChannels()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("webgpu-color-mask"));
        GpuShaderPackage package = CreatePackage(
            TriangleShader, "triangleVertex", "triangleFragment", abiHash);
        GpuTextureHandle target = CreateTarget(backend);
        GpuTextureView targetView = backend.CreateTextureView(target, new(GpuFormat.Rgba8Unorm));
        GpuRasterPipelineHandle pipeline = backend.CreateRasterPipeline(
            new GpuRasterPipelineDescription([
                new(GpuFormat.Rgba8Unorm, GpuColorWriteMask.Red),
            ]),
            package,
            "triangleVertex",
            "triangleFragment",
            abiHash);

        GpuCommandBuffer commands = BeginDraw(
                backend, targetView, null, new(0, 0.4f, 0.8f, 1))
            .SetPipeline(pipeline)
            .Draw(3)
            .EndRendering();
        Submit(backend, commands);
        byte[] pixels = ReadTarget(backend, target);

        AssertPixelNear(pixels, 32, 32, 255, 102, 204, 255);

        DestroyTarget(backend, target, targetView, pipeline);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void TriangleStripRasterizesAQuad()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("webgpu-triangle-strip"));
        GpuShaderPackage package = CreatePackage(StripShader, "stripVertex", "stripFragment", abiHash);
        GpuTextureHandle target = CreateTarget(backend);
        GpuTextureView targetView = backend.CreateTextureView(target, new(GpuFormat.Rgba8Unorm));
        GpuRasterPipelineHandle pipeline = backend.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)])
            {
                Topology = GpuPrimitiveTopology.TriangleStrip,
            },
            package,
            "stripVertex",
            "stripFragment",
            abiHash);

        Draw(backend, targetView, pipeline, null, 4);
        byte[] pixels = ReadTarget(backend, target);

        AssertPixel(pixels, 32, 32, 255, 255, 255, 255);

        DestroyTarget(backend, target, targetView, pipeline);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void FrontFaceSelectsWhichTriangleIsCulled()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("webgpu-culling"));
        GpuShaderPackage package = CreatePackage(
            TriangleShader, "triangleVertex", "triangleFragment", abiHash);
        GpuTextureHandle ccwTarget = CreateTarget(backend);
        GpuTextureHandle cwTarget = CreateTarget(backend);
        GpuTextureHandle frontCullTarget = CreateTarget(backend);
        GpuTextureView ccwView = backend.CreateTextureView(ccwTarget, new(GpuFormat.Rgba8Unorm));
        GpuTextureView cwView = backend.CreateTextureView(cwTarget, new(GpuFormat.Rgba8Unorm));
        GpuTextureView frontCullView = backend.CreateTextureView(frontCullTarget, new(GpuFormat.Rgba8Unorm));
        GpuRasterPipelineHandle ccwPipeline = backend.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)])
            {
                CullMode = GpuCullMode.Back,
                FrontFace = GpuFrontFace.CounterClockwise,
            }, package, "triangleVertex", "triangleFragment", abiHash);
        GpuRasterPipelineHandle cwPipeline = backend.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)])
            {
                CullMode = GpuCullMode.Back,
                FrontFace = GpuFrontFace.Clockwise,
            }, package, "triangleVertex", "triangleFragment", abiHash);
        GpuRasterPipelineHandle frontCullPipeline = backend.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)])
            {
                CullMode = GpuCullMode.Front,
                FrontFace = GpuFrontFace.CounterClockwise,
            }, package, "triangleVertex", "triangleFragment", abiHash);

        Draw(backend, ccwView, ccwPipeline, null, 3);
        Draw(backend, cwView, cwPipeline, null, 3);
        Draw(backend, frontCullView, frontCullPipeline, null, 3);
        byte[] ccwPixels = ReadTarget(backend, ccwTarget);
        byte[] cwPixels = ReadTarget(backend, cwTarget);
        byte[] frontCullPixels = ReadTarget(backend, frontCullTarget);

        Assert.NotEqual(IsCenterColored(ccwPixels), IsCenterColored(cwPixels));
        Assert.NotEqual(IsCenterColored(ccwPixels), IsCenterColored(frontCullPixels));

        backend.DestroyRasterPipeline(frontCullPipeline);
        backend.DestroyRasterPipeline(cwPipeline);
        backend.DestroyRasterPipeline(ccwPipeline);
        backend.DestroyTextureView(frontCullView);
        backend.DestroyTextureView(cwView);
        backend.DestroyTextureView(ccwView);
        backend.DestroyTexture(frontCullTarget);
        backend.DestroyTexture(cwTarget);
        backend.DestroyTexture(ccwTarget);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void UnsupportedPipelineOptionsAreRejected()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("webgpu-unsupported-pipeline"));
        GpuShaderPackage package = CreatePackage(
            TriangleShader, "triangleVertex", "triangleFragment", abiHash);
        GpuRasterPipelineDescription[] descriptions =
        [
            new([new(GpuFormat.Rgba8Unorm)]) { SampleCount = 2 },
            new([new(GpuFormat.Rgba8Unorm)]) { AlphaToCoverage = true },
            new([new(GpuFormat.Rgba8Unorm)]) { SupportsDualSourceBlending = true },
            new([new(GpuFormat.Rgba8Unorm), new(GpuFormat.Rgba8Unorm)]),
        ];

        Assert.All(descriptions, description =>
            Assert.Throws<NotSupportedException>(() => backend.CreateRasterPipeline(
                description, package, "triangleVertex", "triangleFragment", abiHash)));
    }

    private static void Draw(
        IGpuBackend backend,
        GpuTextureView target,
        GpuRasterPipelineHandle pipeline,
        GpuResourceTable? resources,
        uint vertexCount)
    {
        GpuCommandBuffer commands = BeginDraw(backend, target, null).SetPipeline(pipeline);
        if (resources is not null) { commands.SetResourceTable(resources); }
        commands.Draw(vertexCount).EndRendering();
        Submit(backend, commands);
    }

    private static GpuCommandBuffer BeginDraw(
        IGpuBackend backend,
        GpuTextureView target,
        GpuDepthStencilAttachment? depthStencil,
        GpuClearColor clearColor = default)
        => backend.MainQueue.StartCommandRecording()
            .BeginRendering([
                new(target, GpuAttachmentLoadOperation.Clear, GpuAttachmentStoreOperation.Store,
                    clearColor == default ? new(0, 0, 0, 1) : clearColor),
            ], depthStencil)
            .SetViewportAndScissor(
                new(0, 0, TargetWidth, TargetHeight),
                new(0, 0, TargetWidth, TargetHeight));

    private static void Submit(IGpuBackend backend, GpuCommandBuffer commands)
    {
        using GpuSemaphore completion = backend.MainQueue.CreateSemaphore();
        backend.MainQueue.Submit([commands], completion, 1);
        backend.MainQueue.Wait(completion, 1);
    }

    private static GpuTextureHandle CreateTarget(IGpuBackend backend) => backend.CreateTexture(new(
        TargetWidth,
        TargetHeight,
        GpuFormat.Rgba8Unorm,
        GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource));

    private static byte[] ReadTarget(IGpuBackend backend, GpuTextureHandle target)
        => backend.ReadTexture(target, new(TargetWidth, TargetHeight, 4, TargetWidth * 4));

    private static void DestroyTarget(
        IGpuBackend backend,
        GpuTextureHandle target,
        GpuTextureView targetView,
        GpuRasterPipelineHandle pipeline)
    {
        backend.DestroyRasterPipeline(pipeline);
        backend.DestroyTextureView(targetView);
        backend.DestroyTexture(target);
    }

    private static bool IsCenterColored(ReadOnlySpan<byte> pixels)
    {
        int offset = checked((32 * (int)TargetWidth + 32) * 4);
        return pixels[offset] > 200;
    }

    private static GpuShaderPackage CreatePackage(
        string source,
        string vertexEntryPoint,
        string pixelEntryPoint,
        byte[] abiHash)
    {
        byte[] payload = Encoding.UTF8.GetBytes(source);
        byte[] bytes = GpuShaderPackageWriter.Write([
            new(GpuShaderCodeFormat.Wgsl, GpuShaderStage.Vertex, vertexEntryPoint,
                "webgpu", "wgsl", "", abiHash, payload),
            new(GpuShaderCodeFormat.Wgsl, GpuShaderStage.Pixel, pixelEntryPoint,
                "webgpu", "wgsl", "", abiHash, payload),
        ]);
        return GpuShaderPackage.Read(bytes);
    }

    private static void AssertPixel(
        ReadOnlySpan<byte> pixels,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        int offset = checked((y * (int)TargetWidth + x) * 4);
        Assert.Equal(new byte[] { red, green, blue, alpha }, pixels.Slice(offset, 4).ToArray());
    }

    private static void AssertPixelNear(
        ReadOnlySpan<byte> pixels,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        int offset = checked((y * (int)TargetWidth + x) * 4);
        Assert.InRange(pixels[offset], red - 1, red);
        Assert.InRange(pixels[offset + 1], green - 1, green + 1);
        Assert.InRange(pixels[offset + 2], blue - 1, blue + 1);
        Assert.InRange(pixels[offset + 3], alpha - 1, alpha);
    }
}
