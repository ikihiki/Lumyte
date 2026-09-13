using System.Runtime.InteropServices;
using Lumyte.Graphics.Native;
using Lumyte.Graphics.Native.Resources;

namespace Lumyte.Graphics.Tests;

internal static class NativeResourceManagerConformance
{
    internal static async Task UploadMixedPackageAsync(INativeGpuBackend backend, GpuPackagePlacement placement)
    {
        await using var manager = new GpuResourceManager(backend);
        using var packageScope = manager.CreateScope();
        byte[] expectedData = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53];
        byte[] texels = new byte[256];
        new byte[] { 31, 63, 127, 255 }.CopyTo(texels, 0);
        var textureDescription = new NativeGpuTextureDescription(NativeGpuTextureDimension.TwoD,
            1, 1, 1, 1, 1, 1, GpuFormat.Rgba8Unorm,
            NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination);
        var footprint = new NativeGpuTextureCopyFootprint(0, NativeGpuTextureAspect.Color, 0, 1,
            default, new(1, 1, 1), 256, 256);
        var finalLayout = backend.Capabilities.ExplicitTextureTransitions ? GpuTextureLayout.CopySource : GpuTextureLayout.General;
        var plan = new GpuPackagePlan([
            new GpuPackageBuffer("data", new(16), expectedData),
            new GpuPackageTexture("image", textureDescription,
                [new GpuTextureUpload(texels, footprint, 1, 4, afterLayout: finalLayout)])
        ], [new("Data", "data"), new("Image", "image")]);
        GpuPackageRef package = await packageScope.ImportPackageAsync(plan, placement);
        var data = (GpuBufferRef)package.GetExport("Data");
        var image = (GpuTextureRef)package.GetExport("Image");
        byte[] actualData = await manager.ReadBufferAsync(data);
        byte[] actualPixel = await manager.ReadTextureAsync(image, footprint, 1, 4, finalLayout, finalLayout);

        Assert.Equal(expectedData, actualData);
        Assert.Equal(new byte[] { 31, 63, 127, 255 }, actualPixel);
    }

    internal static async Task UpdateAndReadResourcesAsync(INativeGpuBackend backend)
    {
        await using var manager = new GpuResourceManager(backend);
        using var scope = manager.CreateScope();
        GpuBufferRef buffer = scope.CreateBuffer(new(32));
        GpuTextureRef texture = scope.CreateTexture(new(NativeGpuTextureDimension.TwoD,
            2, 2, 1, 1, 1, 1, GpuFormat.Rgba8Unorm,
            NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination));
        byte[] expectedBuffer = [7, 11, 13, 17, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71];
        byte[] expectedTexels = [11, 22, 33, 255, 44, 55, 66, 255, 77, 88, 99, 255, 111, 122, 133, 255];
        byte[] uploadBytes = new byte[264];
        expectedTexels.AsSpan(0, 8).CopyTo(uploadBytes);
        expectedTexels.AsSpan(8, 8).CopyTo(uploadBytes.AsSpan(256));
        var footprint = new NativeGpuTextureCopyFootprint(0, NativeGpuTextureAspect.Color, 0, 1,
            default, new(2, 2, 1), 256, 512);
        GpuTextureLayout finalLayout = backend.Capabilities.ExplicitTextureTransitions
            ? GpuTextureLayout.CopySource : GpuTextureLayout.General;

        await manager.UploadBufferAsync(buffer, expectedBuffer, destinationOffset: 8);
        await manager.UploadTextureAsync(texture, new(uploadBytes, footprint, 2, 8, afterLayout: finalLayout));
        byte[] actualBuffer = await manager.ReadBufferAsync(buffer, offset: 8, length: (ulong)expectedBuffer.Length);
        byte[] actualTexture = await manager.ReadTextureAsync(texture, footprint, 2, 8, finalLayout, finalLayout);
        byte[] actualTexels = [.. actualTexture.AsSpan(0, 8), .. actualTexture.AsSpan(256, 8)];

        Assert.Equal(expectedBuffer, actualBuffer);
        Assert.Equal(expectedTexels, actualTexels);
    }

    internal static async Task CopyWithReleasedScopeAsync(INativeGpuBackend backend)
    {
        await using var manager = new GpuResourceManager(backend);
        using var scope = manager.CreateScope();
        GpuBufferRef upload = scope.CreateBuffer(new(16, NativeGpuMemoryKind.CpuVisible));
        GpuBufferRef temporary = scope.CreateBuffer(new(16));
        GpuBufferRef readback = scope.CreateBuffer(new(16, NativeGpuMemoryKind.Readback));
        using var pin = manager.Pin(readback);
        byte[] expected = [3, 5, 7, 11, 13, 17, 19, 23, 31, 37, 41, 43, 47, 53, 59, 61];
        Marshal.Copy(expected, 0, manager.GetBufferRange(upload).Region.CpuAddress, expected.Length);
        GpuSubmissionToken completion;
        using (var batch = manager.BeginBatch())
        {
            batch.Use(scope);
            NativeGpuCommandBuffer commands = batch.StartCommandRecording();
            commands.CopyMemory(manager.GetBufferRange(upload), manager.GetBufferRange(temporary));
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Copy, GpuAccess.CopyRead);
            commands.CopyMemory(manager.GetBufferRange(temporary), manager.GetBufferRange(readback));
            commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);
            completion = batch.Submit();
        }
        scope.Dispose();

        await completion.WaitAsync();
        manager.Collect();
        byte[] actual = new byte[expected.Length];
        Marshal.Copy(manager.GetBufferRange(readback).Region.CpuAddress, actual, 0, actual.Length);

        Assert.True(completion.IsComplete);
        Assert.Equal(expected, actual);
        pin.Dispose();
        manager.Collect();
        Assert.Equal(0, manager.Statistics.ResourceCount);
        manager.Trim();
    }
}
