using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Lumyte.Shaders;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed class DirectX12RenderingTests
{
    private const string VertexSource = """
        float4 triangleVertex(uint vertexId : SV_VertexID) : SV_Position
        {
            const float2 positions[3] = {
                float2(-0.8, -0.8),
                float2( 0.8, -0.8),
                float2( 0.0,  0.8)
            };
            return float4(positions[vertexId], 0.0, 1.0);
        }
        """;

    private const string PixelSource = """
        float4 trianglePixel() : SV_Target0
        {
            return float4(1.0, 0.2, 0.1, 1.0);
        }
        """;

    private const string TexturedVertexSource = """
        struct VertexOutput
        {
            float4 position : SV_Position;
            float2 uv : TEXCOORD0;
        };

        VertexOutput quadVertex(uint vertexId : SV_VertexID)
        {
            const float2 positions[6] = {
                float2(-1.0, -1.0), float2( 1.0, -1.0), float2( 1.0,  1.0),
                float2(-1.0, -1.0), float2( 1.0,  1.0), float2(-1.0,  1.0)
            };
            const float2 uvs[6] = {
                float2(0.0, 1.0), float2(1.0, 1.0), float2(1.0, 0.0),
                float2(0.0, 1.0), float2(1.0, 0.0), float2(0.0, 0.0)
            };
            VertexOutput output;
            output.position = float4(positions[vertexId], 0.0, 1.0);
            output.uv = uvs[vertexId];
            return output;
        }
        """;

    private const string TexturedPixelSource = """
        Texture2D<float4> textures[64] : register(t0, space0);
        SamplerState samplers[64] : register(s0, space1);
        cbuffer RootData : register(b0, space0)
        {
            uint textureIndex;
            uint samplerIndex;
        };

        float4 quadPixel(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target0
        {
            return textures[textureIndex].Sample(samplers[samplerIndex], uv);
        }
        """;

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void RasterizedTriangleCanBeReadBack()
    {
        using DirectX12Device device = DirectX12Device.Create();
        var textureArena = new GpuPersistentArena(device);
        var bufferArena = new GpuPersistentArena(device);
        var textures = new GpuManualTextureAllocator(device, textureArena);
        var buffers = new GpuManualBufferAllocator(device, bufferArena);
        var description = new GpuTextureDescription(
            64, 64, GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);
        GpuMemoryAllocation textureMemory = textures.AllocateMemory(description);
        GpuTextureHandle texture = textures.CreatePlacedTexture(description, textureMemory);
        GpuTextureView view = textures.CreateView(texture, new(GpuFormat.Rgba8Unorm));
        GpuColorAttachment attachment = textures.ColorAttachment(
            view,
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store,
            new(0, 0, 0, 1));
        var readbackDescription = new GpuBufferDescription(64 * 64 * 4, GpuBufferUsage.CopyDestination);
        GpuMemoryAllocation readbackMemory = buffers.AllocateMemory(readbackDescription, GpuMemoryKind.HostCached);
        GpuBufferHandle readback = buffers.CreatePlacedBuffer(readbackDescription, readbackMemory);
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("directx12-triangle-v1"));
        GpuShaderPackage package = CreatePackage(abiHash);
        GpuRasterPipelineHandle pipeline = device.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]),
            package,
            "triangleVertex",
            "trianglePixel",
            abiHash);

        GpuCommandBuffer commands = device.MainQueue.StartCommandRecording()
            .Barrier(GpuStage.None, GpuStage.ColorOutput)
            .BeginRendering([attachment])
            .SetPipeline(pipeline)
            .SetViewportAndScissor(new(0, 0, 64, 64), new(0, 0, 64, 64))
            .Draw(3)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToMemory(texture, buffers.AddressOf(readback), new(64, 64, 4, 256));
        using GpuSemaphore completion = device.MainQueue.CreateSemaphore();
        device.MainQueue.Submit([commands], completion, 1);
        device.MainQueue.Wait(completion, 1);
        Span<byte> pixels = readbackMemory.MappedBytes()[..(64 * 64 * 4)];

        int center = (32 * 64 + 32) * 4;
        Assert.InRange(pixels[center], 253, 255);
        Assert.InRange(pixels[center + 1], 50, 52);
        Assert.InRange(pixels[center + 2], 24, 27);
        Assert.Equal(255, pixels[center + 3]);
        Assert.Equal([0, 0, 0, 255], pixels[..4].ToArray());

        buffers.Retire(readback, readbackMemory, new(1));
        buffers.Collect(new(1));
        device.DestroyRasterPipeline(pipeline);
        device.DestroyTextureView(view);
        textures.Retire(texture, textureMemory, new(1));
        textures.Collect(new(1));
        textures.VerifyEmpty();
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void UploadedTextureCanBeSampledByAQuad()
    {
        using DirectX12Device device = DirectX12Device.Create();
        var textureArena = new GpuPersistentArena(device);
        var bufferArena = new GpuPersistentArena(device);
        var textures = new GpuManualTextureAllocator(device, textureArena);
        var buffers = new GpuManualBufferAllocator(device, bufferArena);
        var sampledDescription = new GpuTextureDescription(
            2, 2, GpuFormat.Rgba8Unorm,
            GpuTextureUsage.Sampled | GpuTextureUsage.CopyDestination);
        GpuMemoryAllocation sampledMemory = textures.AllocateMemory(sampledDescription);
        GpuTextureHandle sampledTexture = textures.CreatePlacedTexture(sampledDescription, sampledMemory);
        GpuTextureView sampledView = textures.CreateView(sampledTexture, new(GpuFormat.Rgba8Unorm));
        const uint textureIndex = 2;
        const uint samplerIndex = 3;
        SamplerId sampler = device.CreateSampler(default);
        var resources = new GpuResourceTable(4, 4);
        resources.SetTexture(checked((int)textureIndex), sampledView.Id);
        resources.SetSampler(checked((int)samplerIndex), sampler);
        byte[] texels =
        [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255,
        ];
        var uploadDescription = new GpuBufferDescription(16, GpuBufferUsage.CopySource);
        GpuMemoryAllocation uploadMemory = buffers.AllocateMemory(uploadDescription, GpuMemoryKind.HostMapped);
        GpuBufferHandle upload = buffers.CreatePlacedBuffer(uploadDescription, uploadMemory);
        texels.CopyTo(uploadMemory.MappedBytes());
        var targetDescription = new GpuTextureDescription(
            64, 64, GpuFormat.Rgba8Unorm,
            GpuTextureUsage.ColorAttachment | GpuTextureUsage.CopySource);
        GpuMemoryAllocation targetMemory = textures.AllocateMemory(targetDescription);
        GpuTextureHandle target = textures.CreatePlacedTexture(targetDescription, targetMemory);
        GpuTextureView targetView = textures.CreateView(target, new(GpuFormat.Rgba8Unorm));
        GpuColorAttachment attachment = textures.ColorAttachment(
            targetView,
            GpuAttachmentLoadOperation.Clear,
            GpuAttachmentStoreOperation.Store,
            new(0, 0, 0, 1));
        var readbackDescription = new GpuBufferDescription(64 * 64 * 4, GpuBufferUsage.CopyDestination);
        GpuMemoryAllocation readbackMemory = buffers.AllocateMemory(readbackDescription, GpuMemoryKind.HostCached);
        GpuBufferHandle readback = buffers.CreatePlacedBuffer(readbackDescription, readbackMemory);
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("directx12-textured-quad-v1"));
        GpuShaderPackage package = CreatePackage(
            abiHash,
            TexturedVertexSource,
            "quadVertex",
            TexturedPixelSource,
            "quadPixel");
        GpuRasterPipelineHandle pipeline = device.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]),
            package,
            "quadVertex",
            "quadPixel",
            abiHash);

        GpuCommandBuffer commands = device.MainQueue.StartCommandRecording()
            .CopyMemoryToTexture(buffers.AddressOf(upload), sampledTexture, new(2, 2, 4, 8))
            .Barrier(GpuStage.Copy, GpuStage.PixelShader)
            .BeginRendering([attachment])
            .SetPipeline(pipeline)
            .SetResourceTable(resources)
            .SetRootData([.. BitConverter.GetBytes(textureIndex), .. BitConverter.GetBytes(samplerIndex)])
            .SetViewportAndScissor(new(0, 0, 64, 64), new(0, 0, 64, 64))
            .Draw(6)
            .EndRendering()
            .Barrier(GpuStage.ColorOutput, GpuStage.Copy)
            .CopyTextureToMemory(target, buffers.AddressOf(readback), new(64, 64, 4, 256));
        using GpuSemaphore completion = device.MainQueue.CreateSemaphore();
        device.MainQueue.Submit([commands], completion, 1);
        device.MainQueue.Wait(completion, 1);
        Span<byte> pixels = readbackMemory.MappedBytes()[..(64 * 64 * 4)];

        AssertPixel(pixels, 64, 16, 16, 255, 0, 0, 255);
        AssertPixel(pixels, 64, 48, 16, 0, 255, 0, 255);
        AssertPixel(pixels, 64, 16, 48, 0, 0, 255, 255);
        AssertPixel(pixels, 64, 48, 48, 255, 255, 255, 255);

        buffers.Retire(readback, readbackMemory, new(1));
        buffers.Retire(upload, uploadMemory, new(1));
        buffers.Collect(new(1));
        device.DestroyRasterPipeline(pipeline);
        device.DestroySampler(sampler);
        device.DestroyTextureView(sampledView);
        device.DestroyTextureView(targetView);
        textures.Retire(sampledTexture, sampledMemory, new(1));
        textures.Retire(target, targetMemory, new(1));
        textures.Collect(new(1));
        textures.VerifyEmpty();
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void UnsupportedDualSourceBlendIsRejected()
    {
        using DirectX12Device device = DirectX12Device.Create();
        byte[] abiHash = SHA256.HashData(Encoding.UTF8.GetBytes("directx12-dual-source"));
        GpuShaderPackage package = CreatePackage(abiHash);
        var description = new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)])
        {
            SupportsDualSourceBlending = true,
        };

        Assert.Throws<NotSupportedException>(() => device.CreateRasterPipeline(
            description, package, "triangleVertex", "trianglePixel", abiHash));
    }

    private static GpuShaderPackage CreatePackage(byte[] abiHash) => CreatePackage(
        abiHash, VertexSource, "triangleVertex", PixelSource, "trianglePixel");

    private static GpuShaderPackage CreatePackage(
        byte[] abiHash,
        string vertexSource,
        string vertexEntryPoint,
        string pixelSource,
        string pixelEntryPoint)
    {
        byte[] vertex = Compile(vertexSource, vertexEntryPoint, "vs_6_0");
        byte[] pixel = Compile(pixelSource, pixelEntryPoint, "ps_6_0");
        byte[] bytes = GpuShaderPackageWriter.Write(
        [
            new(GpuShaderCodeFormat.Dxil, GpuShaderStage.Vertex, vertexEntryPoint, "directx12", "vs_6_0", "", abiHash, vertex),
            new(GpuShaderCodeFormat.Dxil, GpuShaderStage.Pixel, pixelEntryPoint, "directx12", "ps_6_0", "", abiHash, pixel),
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
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"DXC failed: {errors}");
            }
            return File.ReadAllBytes(output);
        }
        finally
        {
            if (Directory.Exists(directory)) { Directory.Delete(directory, true); }
        }
    }

    private static void AssertPixel(
        ReadOnlySpan<byte> pixels,
        int width,
        int x,
        int y,
        byte red,
        byte green,
        byte blue,
        byte alpha)
    {
        int offset = (y * width + x) * 4;
        Assert.Equal(new byte[] { red, green, blue, alpha }, pixels.Slice(offset, 4).ToArray());
    }
}
