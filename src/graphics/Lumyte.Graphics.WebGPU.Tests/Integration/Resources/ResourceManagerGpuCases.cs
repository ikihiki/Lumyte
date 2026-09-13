using System.Runtime.InteropServices;
using Lumyte.Graphics.Portable.Resources;
using Lumyte.Graphics.Portable.Shaders;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.Tests;

// Source-linked into the Browser host; every resource operation uses the public Portable API.
internal static class ResourceManagerGpuCases
{
    internal static async Task<(byte[] Data, byte[] Pixel, bool Released)> PackageAsync(P.IPortableGpuBackend backend)
    {
        await using var manager = new GpuResourceManager(backend);
        using var scope = manager.CreateScope();
        var description = new P.GpuTextureDescription(P.GpuTextureDimension.Texture2D,
            1, 1, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, P.GpuTextureUsage.CopySource | P.GpuTextureUsage.CopyDestination);
        var footprint = new P.GpuTextureCopyFootprint(0, P.GpuTextureAspect.All, default, new(1, 1, 1));
        var plan = new GpuPackagePlan([
            new GpuPackageBuffer("data", new(8, P.GpuBufferUsage.CopySource | P.GpuBufferUsage.CopyDestination),
                [2, 3, 5, 7, 11, 13, 17, 19], dependencies: ["image"]),
            new GpuPackageTexture("image", description, [new GpuTextureUpload([31, 63, 127, 255], footprint)])
        ], [new("Data", "data"), new("Image", "image")]);
        GpuPackageRef package = await scope.ImportPackageAsync(plan);
        var data = (GpuBufferRef)package.GetExport("Data");
        var image = (GpuTextureRef)package.GetExport("Image");
        using var pin = manager.Pin(data);
        scope.Dispose();
        manager.Collect();

        byte[] actualData = await manager.ReadBufferAsync(data);
        byte[] actualPixel = await manager.ReadTextureAsync(image, footprint);
        pin.Dispose();
        manager.Collect();

        return (actualData, actualPixel, manager.Statistics.ResourceCount == 0);
    }

    internal static async Task<(uint[] Actual, bool Complete)> ComputeAsync(P.IPortableGpuBackend backend)
    {
        const string shader = """
            @group(0) @binding(0) var<storage, read_write> output: array<u32>;
            @compute @workgroup_size(1) fn main() {
                output[0] = 63u;
                output[1] = 127u;
            }
            """;
        var package = new PortableShaderPackage(PortableShaderPackage.CurrentVersion, shader,
            [new(P.GpuShaderStage.Compute, "main")], PortableShaderFeatures.None,
            [new([new(0, P.GpuShaderStage.Compute, new P.GpuBufferBindingLayout(P.GpuBufferBindingType.Storage))])],
            null, [], new([new("Output", 0, 0, P.GpuBindingLayoutKind.Buffer)]), "managed-compute-v1");
        using var program = new PortableShaderLoader(backend).Load(package);
        await using var manager = new GpuResourceManager(backend);
        using var scope = manager.CreateScope();
        GpuBufferRef output = scope.CreateBuffer(new(8, P.GpuBufferUsage.Storage | P.GpuBufferUsage.CopySource));
        GpuBufferRef readback = scope.CreateBuffer(new(8, P.GpuBufferUsage.MapRead | P.GpuBufferUsage.CopyDestination));
        using var pin = manager.Pin(readback);
        GpuBindingsRef bindings = scope.GetBindings(program, 0, new OutputInputs(output));
        P.GpuComputePipelineHandle pipeline = backend.CreateComputePipeline(program.Description);
        try
        {
            GpuSubmissionToken completion;
            using (var batch = manager.BeginBatch())
            {
                batch.Use(bindings);
                batch.Use(readback);
                P.GpuCommandBuffer commands = batch.StartCommandRecording();
                commands.BeginCompute();
                commands.SetComputePipeline(pipeline);
                commands.SetComputeBindings(0, manager.GetBindingsHandle(bindings));
                commands.Dispatch(1);
                commands.EndCompute();
                commands.CopyBuffer(manager.GetBufferRange(output), manager.GetBufferRange(readback));
                completion = batch.Submit();
            }
            scope.Dispose();

            await completion.WaitAsync();
            manager.Collect();
            P.GpuBufferRange range = manager.GetBufferRange(readback);
            using P.GpuMappedBufferRange mapped = await backend.MapBufferAsync(range.Buffer, P.GpuMapMode.Read, range.Offset, range.Length!.Value);
            return (MemoryMarshal.Cast<byte, uint>(mapped.ReadOnlyMemory.Span).ToArray(), completion.IsComplete);
        }
        finally { backend.DestroyComputePipeline(pipeline); }
    }

    private sealed record OutputInputs(GpuBufferRef Output) : IGpuBindingInputs
    { public void Write(GpuBindingWriter writer) => writer.Buffer(0, Output); }
}
