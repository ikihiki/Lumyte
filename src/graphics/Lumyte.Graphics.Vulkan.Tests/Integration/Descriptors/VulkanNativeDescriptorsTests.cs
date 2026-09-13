using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

[Collection("GpuBackend")]
[Trait("Category", "VulkanNativeConformance")]
public sealed unsafe class VulkanNativeDescriptorsTests
{
    [VulkanNativeFact]
    public void MixedResourceAndSamplerHeapsCanBeWrittenAndSubmitted()
    {
        using var resources = new Resources();
        VulkanBackend backend = resources.Backend;
        var texture = resources.Texture(Description());
        var upload = resources.Linear(256, NativeGpuMemoryKind.CpuVisible);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        var heap = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 8);
        var sampler = resources.Descriptors(NativeGpuDescriptorHeapKind.Sampler, 8);
        byte[] expected = Enumerable.Range(0, 256).Select(index => (byte)(index * 31)).ToArray();
        expected.CopyTo(new Span<byte>((void*)upload.CpuAddress, 256));
        backend.WriteTextureDescriptor(heap, 2, View(texture));
        backend.WriteTextureDescriptor(heap, 5, View(texture), NativeGpuTextureDescriptorType.Storage);
        backend.WriteBufferDescriptor(heap, 3, new(upload, 32, 128), NativeGpuBufferAccess.ReadOnly);
        backend.WriteBufferDescriptor(heap, 7, new(readback, 0, 128), NativeGpuBufferAccess.ReadWrite);
        backend.WriteSamplerDescriptor(sampler, 2, new());
        backend.WriteSamplerDescriptor(sampler, 7, new(MinLod: 1.5f, MaxLod: 5.5f,
            CompareEnabled: true, CompareOp: GpuCompareOp.Less));
        var queue = backend.MainQueue;
        using var completion = backend.CreateSemaphore(0);
        using var command = queue.StartCommandRecording();
        command.SetResourceDescriptorHeap(heap);
        command.SetSamplerDescriptorHeap(sampler);
        command.CopyMemoryToTexture(new(upload, 0, 256), texture, Footprint());
        command.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Copy, GpuAccess.CopyRead);
        command.CopyTextureToMemory(texture, new(readback, 0, 256), Footprint());
        command.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

        queue.Submit([command], new(completion, 1));
        command.Dispose();
        completion.WaitCpu(1);

        Assert.Equal(expected, new ReadOnlySpan<byte>((void*)readback.CpuAddress, 256).ToArray());
    }

    [VulkanNativeTheory]
    [InlineData(NativeGpuTextureViewDimension.OneD, NativeGpuTextureDimension.OneD, 1U, 1U)]
    [InlineData(NativeGpuTextureViewDimension.TwoD, NativeGpuTextureDimension.TwoD, 8U, 1U)]
    [InlineData(NativeGpuTextureViewDimension.TwoDArray, NativeGpuTextureDimension.TwoD, 8U, 3U)]
    [InlineData(NativeGpuTextureViewDimension.ThreeD, NativeGpuTextureDimension.ThreeD, 8U, 1U)]
    [InlineData(NativeGpuTextureViewDimension.Cube, NativeGpuTextureDimension.TwoD, 8U, 6U)]
    public void ShaderViewDimensionsCanWriteDescriptors(NativeGpuTextureViewDimension viewDimension,
        NativeGpuTextureDimension dimension, uint height, uint layers)
    {
        using var resources = new Resources();
        var texture = resources.Texture(Description() with
        {
            Dimension = dimension, Height = height, LayerCount = layers,
            Depth = dimension == NativeGpuTextureDimension.ThreeD ? 4U : 1U,
        });
        var heap = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 1);

        resources.Backend.WriteTextureDescriptor(heap, 0, View(texture) with { Dimension = viewDimension, LayerCount = layers });

        Assert.Equal((NativeGpuDescriptorHeapKind.Resource, 1U), (heap.Kind, heap.Capacity));
    }

    [VulkanCubeArrayDescriptorFact]
    public void CubeArrayDescriptorsUseTheOptionalNativeFeature()
    {
        using var resources = new Resources();
        var texture = resources.Texture(Description() with { LayerCount = 12 });
        var heap = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 1);

        resources.Backend.WriteTextureDescriptor(heap, 0, View(texture) with
        {
            Dimension = NativeGpuTextureViewDimension.CubeArray, LayerCount = 12,
        });

        Assert.Equal(1U, heap.Capacity);
    }

    [VulkanAnisotropicDescriptorFact]
    public void AnisotropicSamplersUseTheOptionalNativeFeature()
    {
        using var resources = new Resources();
        var heap = resources.Descriptors(NativeGpuDescriptorHeapKind.Sampler, 1);

        resources.Backend.WriteSamplerDescriptor(heap, 0, new(MinLod: 1.5f, MaxLod: 5.5f, MaxAnisotropy: 4));

        Assert.Equal(1U, heap.Capacity);
    }

    [VulkanNativeTheory]
    [InlineData(NativeGpuTextureAspect.Depth)]
    [InlineData(NativeGpuTextureAspect.Stencil)]
    public void DepthStencilSampledDescriptorsUseTheRequestedAspect(NativeGpuTextureAspect aspect)
    {
        using var resources = new Resources();
        var texture = resources.Texture(Description() with
        {
            Format = GpuFormat.Depth24PlusStencil8,
            Usage = NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.DepthStencilAttachment,
        });
        var heap = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 1);

        resources.Backend.WriteTextureDescriptor(heap, 0, View(texture) with { Format = GpuFormat.Depth24PlusStencil8, Aspect = aspect });

        Assert.Equal(1U, heap.Capacity);
    }

    [VulkanNativeFact]
    public void DestroyedDescriptorHeapDoesNotDestroyItsReferencedResource()
    {
        using var resources = new Resources();
        var texture = resources.Texture(Description());
        var heap = resources.Backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 1);
        resources.Backend.WriteTextureDescriptor(heap, 0, View(texture));

        resources.Backend.DestroyDescriptorHeap(heap);
        var replacement = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 1);
        resources.Backend.WriteTextureDescriptor(replacement, 0, View(texture));

        Assert.Throws<ObjectDisposedException>(() => resources.Backend.WriteTextureDescriptor(heap, 0, View(texture)));
    }

    [VulkanNativeTheory]
    [InlineData(1U)]
    [InlineData(uint.MaxValue)]
    public void DescriptorIndexCannotEscapeItsHostAllocation(uint index)
    {
        using var resources = new Resources();
        var heap = resources.Descriptors(NativeGpuDescriptorHeapKind.Sampler, 1);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => resources.Backend.WriteSamplerDescriptor(heap, index, new()));

        Assert.Equal("index", error.ParamName);
    }

    [VulkanNativeFact]
    public void WrongHeapKindIsRejectedBeforeDescriptorWrites()
    {
        using var resources = new Resources();
        var heap = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 1);

        var error = Assert.Throws<ArgumentException>(() => resources.Backend.WriteSamplerDescriptor(heap, 0, new()));

        Assert.Equal("heap", error.ParamName);
    }

    [VulkanNativeFact]
    public void ForeignDescriptorHeapIsRejectedBeforeNativeBinding()
    {
        using var backend = VulkanBackend.Create();
        using var recording = backend.MainQueue.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => recording.SetResourceDescriptorHeap(new ForeignHeap()));

        Assert.Equal("heap", error.ParamName);
    }

    [VulkanNativeFact]
    public void SubmittedRecordingRejectsFurtherHeapSelections()
    {
        using var resources = new Resources();
        var heap = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 1);
        var queue = resources.Backend.MainQueue;
        using var completion = resources.Backend.CreateSemaphore(0);
        using var recording = queue.StartCommandRecording();
        recording.SetResourceDescriptorHeap(heap);
        queue.Submit([recording], new(completion, 1));
        completion.WaitCpu(1);

        var error = Assert.Throws<InvalidOperationException>(() => recording.SetResourceDescriptorHeap(heap));

        Assert.Contains("only once", error.Message);
    }

    [VulkanNativeFact]
    public void RenderViewDestructionLeavesTheTextureAvailableForAnotherView()
    {
        using var resources = new Resources();
        var texture = resources.Texture(Description());
        var first = resources.Backend.CreateRenderView(View(texture));
        resources.Backend.DestroyRenderView(first);

        var second = resources.Backend.CreateRenderView(View(texture));
        resources.Backend.DestroyRenderView(second);

        Assert.Throws<ObjectDisposedException>(() => resources.Backend.DestroyRenderView(first));
    }

    [VulkanNativeTheory]
    [InlineData(NativeGpuRenderViewFlags.DepthReadOnly)]
    [InlineData(NativeGpuRenderViewFlags.StencilReadOnly)]
    [InlineData(NativeGpuRenderViewFlags.DepthReadOnly | NativeGpuRenderViewFlags.StencilReadOnly)]
    public void RenderViewRetainsRequestedReadOnlyAspects(NativeGpuRenderViewFlags flags)
    {
        using var resources = new Resources();
        var texture = resources.Texture(Description() with
        {
            Format = GpuFormat.Depth24PlusStencil8,
            Usage = NativeGpuTextureUsage.DepthStencilAttachment,
        });
        var view = resources.Backend.CreateRenderView(View(texture) with
        {
            Format = GpuFormat.Depth24PlusStencil8, Aspect = NativeGpuTextureAspect.DepthStencil,
        }, flags);

        resources.Backend.DestroyRenderView(view);

        Assert.Equal(flags, view.Flags);
    }

    [VulkanNativeTheory]
    [InlineData(NativeGpuTextureViewDimension.TwoD, 1U)]
    [InlineData(NativeGpuTextureViewDimension.TwoDArray, 2U)]
    public void ThreeDimensionalAttachmentsCanSelectDepthSlices(NativeGpuTextureViewDimension dimension, uint count)
    {
        using var resources = new Resources();
        var texture = resources.Texture(Description() with { Dimension = NativeGpuTextureDimension.ThreeD, Depth = 4 });

        var view = resources.Backend.CreateRenderView(View(texture) with { Dimension = dimension, BaseLayer = 1, LayerCount = count });
        resources.Backend.DestroyRenderView(view);

        Assert.Equal(NativeGpuRenderViewFlags.None, view.Flags);
    }

    [VulkanNativeFact]
    public void DepthSliceDiscardCannotSilentlyDiscardAnEntireVolume()
    {
        using var resources = new Resources();
        var texture = resources.Texture(Description() with { Dimension = NativeGpuTextureDimension.ThreeD, Depth = 4 });
        using var commands = resources.Backend.MainQueue.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => commands.DiscardTexture(View(texture), GpuTextureLayout.General));

        Assert.Equal("view", error.ParamName);
    }

    [VulkanNativeFact]
    public void ThreeDimensionalViewCanDiscardTheWholeMipVolume()
    {
        using var resources = new Resources();
        var texture = resources.Texture(Description() with { Dimension = NativeGpuTextureDimension.ThreeD, Depth = 4 });
        var queue = resources.Backend.MainQueue;
        using var completion = resources.Backend.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.DiscardTexture(View(texture) with { Dimension = NativeGpuTextureViewDimension.ThreeD }, GpuTextureLayout.General);

        queue.Submit([commands], new(completion, 1));
        completion.WaitCpu(1);

        Assert.True(completion.IsComplete(1));
    }

    private static NativeGpuTextureDescription Description() => new(NativeGpuTextureDimension.TwoD,
        8, 8, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.Storage
        | NativeGpuTextureUsage.ColorAttachment | NativeGpuTextureUsage.CopySource | NativeGpuTextureUsage.CopyDestination);
    private static NativeGpuTextureView View(NativeGpuTextureHandle texture) => new(texture,
        NativeGpuTextureViewDimension.TwoD, GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 0, 1, 0, 1);
    private static NativeGpuTextureCopyFootprint Footprint() => new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(8, 8, 1), 32, 256);
    private sealed class ForeignHeap() : NativeGpuDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 1);

    private sealed class Resources : IDisposable
    {
        private readonly List<NativeGpuHeap> heaps = [];
        private readonly List<NativeGpuLinearRegion> regions = [];
        private readonly List<NativeGpuTextureHandle> textures = [];
        private readonly List<NativeGpuDescriptorHeap> descriptors = [];
        public VulkanBackend Backend { get; } = VulkanBackend.Create();

        public NativeGpuLinearRegion Linear(ulong size, NativeGpuMemoryKind kind)
        {
            var requirement = Backend.GetLinearMemoryRequirements(size, kind);
            var heap = Backend.CreateGpuHeap(requirement.Size, requirement.Alignment, kind, [requirement.Compatibility]);
            heaps.Add(heap);
            var region = Backend.CreateLinearRegion(size, heap, 0);
            regions.Add(region);
            return region;
        }
        public NativeGpuTextureHandle Texture(NativeGpuTextureDescription description)
        {
            var requirement = Backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
            var heap = Backend.CreateGpuHeap(requirement.Size, requirement.Alignment, NativeGpuMemoryKind.GpuOnly, [requirement.Compatibility]);
            heaps.Add(heap);
            var texture = Backend.CreateTexture(description, heap, 0);
            textures.Add(texture);
            return texture;
        }
        public NativeGpuDescriptorHeap Descriptors(NativeGpuDescriptorHeapKind kind, uint capacity)
        {
            var heap = Backend.CreateDescriptorHeap(kind, capacity);
            descriptors.Add(heap);
            return heap;
        }
        public void Dispose()
        {
            foreach (var descriptor in descriptors) { Backend.DestroyDescriptorHeap(descriptor); }
            foreach (var texture in textures) { Backend.DestroyTexture(texture); }
            foreach (var region in regions) { Backend.DestroyLinearRegion(region); }
            foreach (var heap in heaps) { Backend.DestroyGpuHeap(heap); }
            Backend.Dispose();
        }
    }
}

internal sealed class VulkanCubeArrayDescriptorFactAttribute : FactAttribute
{
    public VulkanCubeArrayDescriptorFactAttribute()
    {
        try
        {
            using var backend = VulkanBackend.Create();
            if (!backend.SupportsImageCubeArray) { Skip = "Vulkan imageCubeArray is unavailable."; }
        }
        catch (NotSupportedException exception) { Skip = exception.Message; }
    }
}

internal sealed class VulkanAnisotropicDescriptorFactAttribute : FactAttribute
{
    public VulkanAnisotropicDescriptorFactAttribute()
    {
        try
        {
            using var backend = VulkanBackend.Create();
            if (!backend.SupportsSamplerAnisotropy) { Skip = "Vulkan samplerAnisotropy is unavailable."; }
        }
        catch (NotSupportedException exception) { Skip = exception.Message; }
    }
}
