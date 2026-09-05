using System.Text;
using Lumyte.Graphics.Shader;

namespace Lumyte.Graphics.WebGPU.Tests;

[Collection("GpuBackend")]
public sealed class WebGpuImmediateDataTests
{
    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void ImmediateOnlyComputeUsesRootData()
    {
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, Run(resetPipeline: false));
    }

    [Fact]
    [Trait("Category", "WebGpuConformance")]
    public void PipelineBindingClearsImmediateData()
    {
        Assert.Equal(new byte[4], Run(resetPipeline: true));
    }

    private static byte[] Run(bool resetPipeline)
    {
        using var backend = WebGpuDevice.Create();
        const string source = """
            struct Root { color: vec4f, padding0: vec4f, padding1: vec4f, tail: vec4f }
            var<immediate> root: Root;
            @group(3) @binding(0) var outputTexture: texture_storage_2d<rgba8unorm, write>;
            @compute @workgroup_size(1)
            fn main() { textureStore(outputTexture, vec2i(0, 0), root.color + root.tail); }
            """;
        var package = GpuShaderPackage.Read(GpuShaderPackageWriter.Write([
            new(GpuShaderCodeFormat.Wgsl, GpuShaderStage.Compute, "main", "webgpu", "wgsl", "",
                GpuShaderBindingConvention.AbiHash, Encoding.UTF8.GetBytes(source)),
        ]));
        var texture = backend.CreateTexture(new(1, 1, GpuFormat.Rgba8Unorm, GpuTextureUsage.Storage | GpuTextureUsage.CopySource));
        var view = backend.CreateTextureView(texture, new(GpuFormat.Rgba8Unorm, Access: GpuTextureViewAccess.ReadWrite));
        var pipeline = backend.CreateComputePipeline(package, "main", GpuShaderBindingConvention.AbiHash);
        try
        {
            var table = new GpuResourceTable(0, 0, storageTextureSlotCount: 1);
            table.SetStorageTexture(0, view.Id);
            using var commands = backend.MainQueue.StartCommandRecording();
            byte[] root = new byte[64];
            BitConverter.GetBytes(1f).CopyTo(root, 0);
            BitConverter.GetBytes(1f).CopyTo(root, 12);
            commands.SetComputePipeline(pipeline).SetComputeResourceTable(table).SetComputeRootData(root);
            if (resetPipeline) { commands.SetComputePipeline(pipeline).SetComputeResourceTable(table); }
            commands.Dispatch(1);
            using var completion = backend.MainQueue.CreateSemaphore();
            backend.MainQueue.Submit([commands], completion, 10);
            backend.MainQueue.Wait(completion, 5);

            return backend.ReadTexture(texture, new(1, 1, 4, 4));
        }
        finally
        {
            backend.DestroyComputePipeline(pipeline);
            backend.DestroyTextureView(view);
            backend.DestroyTexture(texture);
        }
    }
}
