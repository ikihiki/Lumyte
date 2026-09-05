using System.Text;

using Lumyte.Graphics.Shader;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
public sealed class WebGpuDeviceTests
{
    private const string StorageTextureComputeSource = """
        @group(3) @binding(0)
        var outputTexture : texture_storage_2d<rgba8unorm, write>;
        @group(4) @binding(0)
        var<storage, read_write> scratchBuffer : array<vec4<f32>>;

        @compute @workgroup_size(1, 1, 1)
        fn writeStorageTexture(@builtin(global_invocation_id) threadId : vec3<u32>) {
            let index = threadId.y * 2u + threadId.x;
            scratchBuffer[index] = vec4<f32>(0.25, 0.5, 0.75, 1.0);
            textureStore(
                outputTexture,
                vec2<i32>(threadId.xy),
                scratchBuffer[index]);
        }
        """;

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void BackendCanBeCreated()
    {
        using IGpuBackend backend = WebGpuBackend.Create();

        Assert.NotNull(backend.MainQueue);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void BackendExposesImplementedCapabilities()
    {
        using IGpuBackend backend = WebGpuBackend.Create();

        Assert.Equal(
            GpuBackendCapabilities.DeviceOwnedResources
            | GpuBackendCapabilities.RasterPipeline
            | GpuBackendCapabilities.ComputePipeline,
            backend.Capabilities);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void DeviceIssuesDistinctLogicalSamplerIds()
    {
        using IGpuBackend backend = WebGpuBackend.Create();

        SamplerId first = backend.CreateSampler(default);
        SamplerId second = backend.CreateSampler(new(GpuSamplerFilter.Linear, GpuSamplerFilter.Linear));

        Assert.False(first.IsNull);
        Assert.False(second.IsNull);
        Assert.NotEqual(first, second);

        backend.DestroySampler(second);
        backend.DestroySampler(first);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void DeviceIssuesDistinctLogicalTextureIds()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        GpuTextureHandle texture = backend.CreateTexture(
            new(2, 2, GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled));

        GpuTextureView first = backend.CreateTextureView(texture, new(GpuFormat.Rgba8Unorm));
        GpuTextureView second = backend.CreateTextureView(texture, new(GpuFormat.Rgba8Unorm));

        Assert.False(first.Id.IsNull);
        Assert.False(second.Id.IsNull);
        Assert.NotEqual(first.Id, second.Id);

        backend.DestroyTextureView(second);
        backend.DestroyTextureView(first);
        backend.DestroyTexture(texture);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void TextureRoundTripPreservesPixels()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        byte[] expected =
        [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255,
        ];

        GpuTextureHandle texture = backend.CreateTexture(new(
            2,
            2,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.CopyDestination | GpuTextureUsage.CopySource));

        backend.WriteTexture(texture, expected, new(2, 2, 4, 8));
        byte[] actual = backend.ReadTexture(texture, new(2, 2, 4, 8));

        Assert.Equal(expected, actual);

        backend.DestroyTexture(texture);
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void ComputeWritesStorageResourcesThroughCommonResourceTable()
    {
        using IGpuBackend backend = WebGpuBackend.Create();
        var description = new GpuTextureDescription(
            2,
            2,
            GpuFormat.Rgba8Unorm,
            GpuTextureUsage.Storage | GpuTextureUsage.CopySource);
        GpuTextureHandle texture = backend.CreateTexture(description);
        GpuTextureView view = backend.CreateTextureView(
            texture,
            new(GpuFormat.Rgba8Unorm, Access: GpuTextureViewAccess.ReadWrite));
        GpuBufferHandle writable = backend.CreateBuffer(new(64, GpuBufferUsage.Storage));
        GpuBufferView writableView = backend.CreateBufferView(
            writable,
            new(Access: GpuBufferViewAccess.ReadWrite));
        byte[] abiHash = GpuShaderBindingConvention.AbiHash.ToArray();
        GpuShaderPackage package = GpuShaderPackage.Read(GpuShaderPackageWriter.Write([
            new(
                GpuShaderCodeFormat.Wgsl,
                GpuShaderStage.Compute,
                "writeStorageTexture",
                "webgpu",
                "wgsl",
                "",
                abiHash,
                Encoding.UTF8.GetBytes(StorageTextureComputeSource)),
        ]));
        GpuComputePipelineHandle pipeline = backend.CreateComputePipeline(
            package,
            "writeStorageTexture",
            abiHash);
        var resources = new GpuResourceTable(0, 0, 0, 1, 1);
        resources.SetStorageTexture(0, view.Id);
        resources.SetWritableBuffer(0, writableView.Id);

        GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording()
            .SetComputePipeline(pipeline)
            .SetComputeResourceTable(resources)
            .Dispatch(2, 2)
            .Barrier(GpuStage.ComputeShader, GpuStage.Copy);
        using GpuSemaphore completion = backend.MainQueue.CreateSemaphore();
        backend.MainQueue.Submit([commands], completion, 1);
        backend.MainQueue.Wait(completion, 1);
        byte[] actual = backend.ReadTexture(texture, new(2, 2, 4, 8));

        backend.DestroyComputePipeline(pipeline);
        backend.DestroyTextureView(view);
        backend.DestroyTexture(texture);
        backend.DestroyBufferView(writableView);
        backend.DestroyBuffer(writable);

        byte[] expected = Enumerable.Repeat(new byte[] { 64, 128, 191, 255 }, 4)
            .SelectMany(static value => value)
            .ToArray();
        Assert.True(
            actual.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) <= 1),
            $"Expected [{string.Join(", ", expected)}] within 1, but was [{string.Join(", ", actual)}].");
    }
}
