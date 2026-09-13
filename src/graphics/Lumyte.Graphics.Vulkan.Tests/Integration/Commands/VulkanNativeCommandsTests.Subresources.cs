using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe partial class VulkanNativeCommandsTests
{
    [VulkanSeparateDepthStencilFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DiscardingDepthPreservesStencil()
    {
        using var resources = new Resources();
        var upload = resources.Linear(4096, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(4096, NativeGpuMemoryKind.Readback);
        var texture = resources.Texture(TextureDescription() with { Format = GpuFormat.Depth24PlusStencil8 });
        byte[] stencil = Pattern(64, 91);
        stencil.CopyTo(Bytes(upload)[512..]);
        Bytes(upload)[..256].Clear();
        for (int index = 0; index < 64; index++)
        {
            BitConverter.TryWriteBytes(Bytes(upload).Slice(1024 + index * 4, 4), (uint)(index * 65537));
        }
        var depthFootprint = Footprint() with { Aspect = NativeGpuTextureAspect.Depth };
        var stencilFootprint = Footprint() with { Aspect = NativeGpuTextureAspect.Stencil, RowPitch = 8, ImagePitch = 64 };
        var queue = resources.Backend.MainQueue;
        using var completion = resources.Backend.CreateSemaphore(0);
        using var write = queue.StartCommandRecording();
        using var discard = queue.StartCommandRecording();
        write.CopyMemoryToTexture(new(upload, 0, 256), texture, depthFootprint);
        CopyDependency(write);
        write.CopyMemoryToTexture(new(upload, 512, 64), texture, stencilFootprint);
        AliasDependency(discard);
        discard.DiscardTexture(View(texture) with { Format = GpuFormat.Depth24PlusStencil8, Aspect = NativeGpuTextureAspect.Depth }, GpuTextureLayout.General);
        discard.CopyMemoryToTexture(new(upload, 1024, 256), texture, depthFootprint);
        CopyDependency(discard);
        discard.CopyTextureToMemory(texture, new(readback, 512, 64), stencilFootprint);
        discard.CopyTextureToMemory(texture, new(readback, 0, 256), depthFootprint);
        HostDependency(discard);

        queue.Submit([write, discard], new(completion, 1));
        completion.WaitCpu(1);

        Assert.Equal(stencil, Bytes(readback)[512..576].ToArray());
        Assert.Equal(Enumerable.Range(0, 64).Select(index => (uint)(index * 65537)).ToArray(),
            Enumerable.Range(0, 64).Select(index => BitConverter.ToUInt32(Bytes(readback).Slice(index * 4, 4)) & 0xFFFFFF).ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void TextureCopyPreservesMipLayerAndOrigin()
    {
        using var resources = new Resources();
        var upload = resources.Linear(4096, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(4096, NativeGpuMemoryKind.Readback);
        var texture = resources.Texture(TextureDescription() with { Width = 32, Height = 16, MipCount = 4, LayerCount = 3 });
        byte[] expected = Pattern(96, 73);
        expected.CopyTo(Bytes(upload)[128..]);
        var footprint = new NativeGpuTextureCopyFootprint(1, NativeGpuTextureAspect.Color, 1, 1,
            new(4, 2, 0), new(5, 3, 1), 32, 128);
        var queue = resources.Backend.MainQueue;
        using var completion = resources.Backend.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.CopyMemoryToTexture(new(upload, 128, 128), texture, footprint);
        CopyDependency(commands);
        commands.CopyTextureToMemory(texture, new(readback, 256, 128), footprint);
        HostDependency(commands);

        queue.Submit([commands], new(completion, 1));
        completion.WaitCpu(1);

        Assert.Equal(Enumerable.Range(0, 3).SelectMany(row => expected.Skip(row * 32).Take(20)).ToArray(),
            Enumerable.Range(0, 3).SelectMany(row => Bytes(readback).Slice(256 + row * 32, 20).ToArray()).ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DiscardingOneLayerPreservesTheOtherLayer()
    {
        using var resources = new Resources();
        var upload = resources.Linear(4096, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(4096, NativeGpuMemoryKind.Readback);
        var texture = resources.Texture(TextureDescription() with { LayerCount = 2 });
        byte[] original = Pattern(512, 17);
        byte[] replacement = Pattern(256, 113);
        original.CopyTo(Bytes(upload));
        replacement.CopyTo(Bytes(upload)[512..]);
        var queue = resources.Backend.MainQueue;
        using var completion = resources.Backend.CreateSemaphore(0);
        using var write = queue.StartCommandRecording();
        using var replace = queue.StartCommandRecording();
        var bothLayers = Footprint() with { LayerCount = 2 };
        write.CopyMemoryToTexture(new(upload, 0, 512), texture, bothLayers);
        AliasDependency(replace);
        replace.DiscardTexture(View(texture) with { Dimension = NativeGpuTextureViewDimension.TwoDArray, BaseLayer = 1 }, GpuTextureLayout.General);
        replace.CopyMemoryToTexture(new(upload, 512, 256), texture, Footprint() with { BaseLayer = 1 });
        CopyDependency(replace);
        replace.CopyTextureToMemory(texture, new(readback, 0, 512), bothLayers);
        HostDependency(replace);

        queue.Submit([write, replace], new(completion, 1));
        completion.WaitCpu(1);

        Assert.Equal(original.Take(256).Concat(replacement).ToArray(), Bytes(readback)[..512].ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void FreshPartialDiscardInitializesBeforeAllLayerUses()
    {
        using var resources = new Resources();
        var upload = resources.Linear(4096, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(4096, NativeGpuMemoryKind.Readback);
        var texture = resources.Texture(TextureDescription() with { LayerCount = 2 });
        byte[] expected = Pattern(512, 67);
        expected.CopyTo(Bytes(upload));
        var queue = resources.Backend.MainQueue;
        using var completion = resources.Backend.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.DiscardTexture(View(texture) with { Dimension = NativeGpuTextureViewDimension.TwoDArray, BaseLayer = 1 }, GpuTextureLayout.General);
        commands.CopyMemoryToTexture(new(upload, 0, 256), texture, Footprint());
        commands.CopyMemoryToTexture(new(upload, 256, 256), texture, Footprint() with { BaseLayer = 1 });
        CopyDependency(commands);
        commands.CopyTextureToMemory(texture, new(readback, 0, 512), Footprint() with { LayerCount = 2 });
        HostDependency(commands);

        queue.Submit([commands], new(completion, 1));
        completion.WaitCpu(1);

        Assert.Equal(expected, Bytes(readback)[..512].ToArray());
    }
}
