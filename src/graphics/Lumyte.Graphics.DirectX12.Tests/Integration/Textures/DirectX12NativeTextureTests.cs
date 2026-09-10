using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
public sealed class DirectX12NativeTextureTests
{
    [Theory]
    [InlineData(NativeGpuTextureDimension.OneD)]
    [InlineData(NativeGpuTextureDimension.TwoD)]
    [InlineData(NativeGpuTextureDimension.ThreeD)]
    [Trait("Category", "DirectX12Conformance")]
    public void TextureDimensionsCanBePlaced(NativeGpuTextureDimension dimension)
    {
        NativeGpuTextureDescription description = Description with
        {
            Dimension = dimension,
            Height = dimension == NativeGpuTextureDimension.OneD ? 1u : 16u,
            Depth = dimension == NativeGpuTextureDimension.ThreeD ? 4u : 1u,
            LayerCount = dimension == NativeGpuTextureDimension.ThreeD ? 1u : 6u,
            MipCount = 3,
        };

        CreateAndDestroy(description);
    }

    [Theory]
    [InlineData(GpuFormat.Rgba8Unorm)]
    [InlineData(GpuFormat.Bgra8Unorm)]
    [InlineData(GpuFormat.R32Float)]
    [InlineData(GpuFormat.D32Float)]
    [InlineData(GpuFormat.Rgba8UnormSrgb)]
    [InlineData(GpuFormat.Bgra8UnormSrgb)]
    [InlineData(GpuFormat.R8Unorm)]
    [InlineData(GpuFormat.Rg8Unorm)]
    [InlineData(GpuFormat.Depth24PlusStencil8)]
    [Trait("Category", "DirectX12Conformance")]
    public void MutableFormatsCanBePlaced(GpuFormat format)
    {
        NativeGpuTextureDescription description = Description with { Format = format, MutableFormat = true };
        if (format is GpuFormat.D32Float or GpuFormat.Depth24PlusStencil8)
        {
            description = description with
            {
                Usage = NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.DepthStencilAttachment,
            };
        }

        CreateAndDestroy(description);
    }

    [Theory]
    [InlineData(GpuFormat.D32Float)]
    [InlineData(GpuFormat.Depth24PlusStencil8)]
    [Trait("Category", "DirectX12Conformance")]
    public void SampledDepthCanBePlacedWithoutMutableFormats(GpuFormat format)
    {
        CreateAndDestroy(Description with
        {
            Format = format,
            Usage = NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.DepthStencilAttachment,
        });
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void MultisampleAttachmentCanBePlaced()
    {
        CreateAndDestroy(Description with { SampleCount = 4, Usage = NativeGpuTextureUsage.ColorAttachment });
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void LinearSampledAndAttachmentResourcesCanShareOneHeap()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuTextureDescription sampled = Description;
        NativeGpuTextureDescription color = Description with { Usage = NativeGpuTextureUsage.ColorAttachment };
        NativeGpuTextureDescription depth = Description with
        {
            Format = GpuFormat.D32Float, Usage = NativeGpuTextureUsage.DepthStencilAttachment,
        };
        NativeGpuMemoryRequirements linearRequirements = backend.GetLinearMemoryRequirements(256, NativeGpuMemoryKind.GpuOnly);
        NativeGpuMemoryRequirements sampledRequirements = backend.GetTextureMemoryRequirements(sampled, NativeGpuMemoryKind.GpuOnly);
        NativeGpuMemoryRequirements colorRequirements = backend.GetTextureMemoryRequirements(color, NativeGpuMemoryKind.GpuOnly);
        NativeGpuMemoryRequirements depthRequirements = backend.GetTextureMemoryRequirements(depth, NativeGpuMemoryKind.GpuOnly);
        ulong alignment = Math.Max(Math.Max(linearRequirements.Alignment, sampledRequirements.Alignment),
            Math.Max(colorRequirements.Alignment, depthRequirements.Alignment));
        ulong sampledOffset = Align(linearRequirements.Size, sampledRequirements.Alignment);
        ulong colorOffset = Align(checked(sampledOffset + sampledRequirements.Size), colorRequirements.Alignment);
        ulong depthOffset = Align(checked(colorOffset + colorRequirements.Size), depthRequirements.Alignment);
        ulong size = Align(checked(depthOffset + depthRequirements.Size), alignment);
        NativeGpuHeap heap = backend.CreateGpuHeap(size, alignment, NativeGpuMemoryKind.GpuOnly,
            [linearRequirements.Compatibility, sampledRequirements.Compatibility,
                colorRequirements.Compatibility, depthRequirements.Compatibility]);
        try
        {
            NativeGpuLinearRegion linear = backend.CreateLinearRegion(256, heap, 0);
            try
            {
                NativeGpuTextureHandle sampledTexture = backend.CreateTexture(sampled, heap, sampledOffset);
                try
                {
                    NativeGpuTextureHandle colorTexture = backend.CreateTexture(color, heap, colorOffset);
                    try
                    {
                        NativeGpuTextureHandle depthTexture = backend.CreateTexture(depth, heap, depthOffset);
                        try { Assert.NotSame(sampledTexture, depthTexture); }
                        finally { backend.DestroyTexture(depthTexture); }
                    }
                    finally { backend.DestroyTexture(colorTexture); }
                }
                finally { backend.DestroyTexture(sampledTexture); }
            }
            finally { backend.DestroyLinearRegion(linear); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void DestroyedTexturesCanBeReplacedInTheSameHeap()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuMemoryRequirements requirements = backend.GetTextureMemoryRequirements(Description, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = backend.CreateGpuHeap(checked(requirements.Size * 2), requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        try
        {
            NativeGpuTextureHandle first = backend.CreateTexture(Description, heap, requirements.Size);
            backend.DestroyTexture(first);

            NativeGpuTextureHandle replacement = backend.CreateTexture(Description, heap, requirements.Size);
            try { Assert.NotSame(first, replacement); }
            finally { backend.DestroyTexture(replacement); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void ForeignTextureDestructionPreservesTheOwnersResource()
    {
        using DirectX12Backend owner = DirectX12Backend.Create();
        using DirectX12Backend other = DirectX12Backend.Create();
        NativeGpuMemoryRequirements requirements = owner.GetTextureMemoryRequirements(Description, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = owner.CreateGpuHeap(requirements.Size, requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        try
        {
            NativeGpuTextureHandle texture = owner.CreateTexture(Description, heap, 0);
            try
            {
                ArgumentException error = Assert.Throws<ArgumentException>(() => other.DestroyTexture(texture));

                Assert.Equal("texture", error.ParamName);
            }
            finally { owner.DestroyTexture(texture); }
        }
        finally { owner.DestroyGpuHeap(heap); }
    }

    private static void CreateAndDestroy(NativeGpuTextureDescription description)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuMemoryRequirements requirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = backend.CreateGpuHeap(checked(requirements.Size * 2), requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        try
        {
            NativeGpuTextureHandle texture = backend.CreateTexture(description, heap, requirements.Size);
            try { Assert.NotNull(texture); }
            finally { backend.DestroyTexture(texture); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    private static ulong Align(ulong size, ulong alignment) => checked((size + alignment - 1) / alignment * alignment);

    private static NativeGpuTextureDescription Description => new(
        NativeGpuTextureDimension.TwoD, 32, 16, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.Sampled);
}
