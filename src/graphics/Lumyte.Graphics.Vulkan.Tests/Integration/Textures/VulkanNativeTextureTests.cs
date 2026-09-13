using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
public sealed class VulkanNativeTextureTests
{
    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [InlineData(GpuFormat.Rgba8Unorm)]
    [InlineData(GpuFormat.Bgra8Unorm)]
    [InlineData(GpuFormat.Rgba8UnormSrgb)]
    [InlineData(GpuFormat.Bgra8UnormSrgb)]
    [InlineData(GpuFormat.R8Unorm)]
    [InlineData(GpuFormat.Rg8Unorm)]
    [InlineData(GpuFormat.R32Float)]
    [InlineData(GpuFormat.D32Float)]
    [InlineData(GpuFormat.Depth24PlusStencil8)]
    public void TextureFormatsCanBePlaced(GpuFormat format)
    {
        var usage = format is GpuFormat.D32Float or GpuFormat.Depth24PlusStencil8
            ? NativeGpuTextureUsage.DepthStencilAttachment | NativeGpuTextureUsage.Sampled
            : NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination;
        using var backend = VulkanBackend.Create();

        PlaceAndDestroy(backend, Description() with { Format = format, Usage = usage });
    }

    public static IEnumerable<object[]> TextureShapes()
    {
        yield return [new NativeGpuTextureDescription(NativeGpuTextureDimension.OneD,
            32, 1, 1, 4, 3, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.Sampled)];
        yield return [Description() with { Width = 32, Height = 16, MipCount = 5, LayerCount = 7 }];
        yield return [Description() with { MipCount = 5, LayerCount = 12 }];
        yield return [new NativeGpuTextureDescription(NativeGpuTextureDimension.ThreeD,
            32, 16, 8, 4, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.ColorAttachment)];
        yield return [Description() with { SampleCount = 4, Usage = NativeGpuTextureUsage.ColorAttachment }];
        yield return [Description() with { MutableFormat = true }];
    }

    [VulkanNativeTheory]
    [Trait("Category", "VulkanNativeConformance")]
    [MemberData(nameof(TextureShapes))]
    public void TextureShapesCanBePlaced(NativeGpuTextureDescription description)
    {
        using var backend = VulkanBackend.Create();

        PlaceAndDestroy(backend, description);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void LinearDataAndTextureShareOneCallerOwnedHeap()
    {
        using var backend = VulkanBackend.Create();
        var description = Description();
        var linearRequirements = backend.GetLinearMemoryRequirements(4096, NativeGpuMemoryKind.GpuOnly);
        var textureRequirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        ulong alignment = Math.Max(linearRequirements.Alignment, textureRequirements.Alignment);
        ulong textureOffset = Align(linearRequirements.Size, textureRequirements.Alignment);
        ulong size = Align(checked(textureOffset + textureRequirements.Size), alignment);
        var heap = backend.CreateGpuHeap(size, alignment, NativeGpuMemoryKind.GpuOnly,
            [linearRequirements.Compatibility, textureRequirements.Compatibility]);
        try
        {
            var region = backend.CreateLinearRegion(4096, heap, 0);
            try
            {
                var texture = backend.CreateTexture(description, heap, textureOffset);
                try
                {
                    Assert.NotEqual(0ul, region.GpuAddress);
                    Assert.NotNull(texture);
                }
                finally { backend.DestroyTexture(texture); }
            }
            finally { backend.DestroyLinearRegion(region); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void MultipleTexturesCanOccupyIndependentOffsets()
    {
        using var backend = VulkanBackend.Create();
        var firstDescription = Description();
        var secondDescription = Description() with { Width = 64, Height = 16, MipCount = 5, LayerCount = 3 };
        var firstRequirements = backend.GetTextureMemoryRequirements(firstDescription, NativeGpuMemoryKind.GpuOnly);
        var secondRequirements = backend.GetTextureMemoryRequirements(secondDescription, NativeGpuMemoryKind.GpuOnly);
        ulong alignment = Math.Max(firstRequirements.Alignment, secondRequirements.Alignment);
        ulong secondOffset = Align(firstRequirements.Size, secondRequirements.Alignment);
        ulong size = Align(checked(secondOffset + secondRequirements.Size), alignment);
        var heap = backend.CreateGpuHeap(size, alignment, NativeGpuMemoryKind.GpuOnly,
            [firstRequirements.Compatibility, secondRequirements.Compatibility]);
        try
        {
            var first = backend.CreateTexture(firstDescription, heap, 0);
            try
            {
                var second = backend.CreateTexture(secondDescription, heap, secondOffset);
                try { Assert.NotSame(first, second); }
                finally { backend.DestroyTexture(second); }
            }
            finally { backend.DestroyTexture(first); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DestroyedTexturesLeaveTheirHeapAvailableForReuse()
    {
        using var backend = VulkanBackend.Create();
        var description = Description();
        var requirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        var heap = backend.CreateGpuHeap(requirements.Size, requirements.Alignment, NativeGpuMemoryKind.GpuOnly,
            [requirements.Compatibility]);
        try
        {
            var first = backend.CreateTexture(description, heap, 0);
            backend.DestroyTexture(first);

            var replacement = backend.CreateTexture(description, heap, 0);
            try { Assert.NotSame(first, replacement); }
            finally { backend.DestroyTexture(replacement); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void TextureCannotBeDestroyedTwice()
    {
        using var backend = VulkanBackend.Create();
        var description = Description();
        var requirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        var heap = backend.CreateGpuHeap(requirements.Size, requirements.Alignment, NativeGpuMemoryKind.GpuOnly,
            [requirements.Compatibility]);
        try
        {
            var texture = backend.CreateTexture(description, heap, 0);
            backend.DestroyTexture(texture);

            Assert.Throws<ObjectDisposedException>(() => backend.DestroyTexture(texture));
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignTexturesCannotBeDestroyed()
    {
        using var backend = VulkanBackend.Create();

        var exception = Assert.Throws<ArgumentException>(() => backend.DestroyTexture(new ForeignTexture()));

        Assert.Equal("texture", exception.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void AnotherVulkanDevicesTextureCannotBeDestroyed()
    {
        using var backend = VulkanBackend.Create();
        using var other = VulkanBackend.Create();
        var description = Description();
        var requirements = other.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        var heap = other.CreateGpuHeap(requirements.Size, requirements.Alignment, NativeGpuMemoryKind.GpuOnly,
            [requirements.Compatibility]);
        try
        {
            var texture = other.CreateTexture(description, heap, 0);
            try
            {
                var exception = Assert.Throws<ArgumentException>(() => backend.DestroyTexture(texture));

                Assert.Equal("texture", exception.ParamName);
            }
            finally { other.DestroyTexture(texture); }
        }
        finally { other.DestroyGpuHeap(heap); }
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignHeapsCannotReceiveTextures()
    {
        using var backend = VulkanBackend.Create();

        var exception = Assert.Throws<ArgumentException>(() => backend.CreateTexture(Description(), new ForeignHeap(), 0));

        Assert.Equal("heap", exception.ParamName);
    }

    private static void PlaceAndDestroy(VulkanBackend backend, NativeGpuTextureDescription description)
    {
        var requirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        Assert.True(requirements.Size > 0, "The image must reserve backing memory.");
        Assert.Equal(0ul, requirements.Size % requirements.Alignment);
        var heap = backend.CreateGpuHeap(requirements.Size, requirements.Alignment, NativeGpuMemoryKind.GpuOnly,
            [requirements.Compatibility]);
        try
        {
            var texture = backend.CreateTexture(description, heap, 0);
            backend.DestroyTexture(texture);
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    private static NativeGpuTextureDescription Description()
        => new(NativeGpuTextureDimension.TwoD, 16, 16, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.Sampled);

    private static ulong Align(ulong value, ulong alignment) => checked((value + alignment - 1) / alignment * alignment);

    private sealed class ForeignTexture : NativeGpuTextureHandle;
    private sealed class ForeignHeap() : NativeGpuHeap(4096, 256, NativeGpuMemoryKind.GpuOnly);
}
