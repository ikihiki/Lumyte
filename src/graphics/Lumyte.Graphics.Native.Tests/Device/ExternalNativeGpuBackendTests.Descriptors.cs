namespace Lumyte.Graphics.Native.Tests.Device;

public sealed partial class ExternalNativeGpuBackendTests
{
    [Fact]
    public void ConsumerCreatesOpaqueRenderViewsWithSubresourcesAndFlags()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeDescriptors: observed.Add);
        (NativeGpuHeap heap, NativeGpuTextureHandle texture) = CreateTextureForViews(backend);
        var view = new NativeGpuTextureView(texture, NativeGpuTextureViewDimension.TwoDArray,
            GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.DepthStencil, 2, 1, 3, 2);
        const NativeGpuRenderViewFlags flags = NativeGpuRenderViewFlags.DepthReadOnly | NativeGpuRenderViewFlags.StencilReadOnly;

        try
        {
            NativeGpuRenderViewHandle renderView = backend.CreateRenderView(view, flags);
            backend.DestroyRenderView(renderView);

            Assert.Equal(flags, renderView.Flags);
            Assert.Collection(observed,
                value => Assert.Equal(new RenderViewCreation(view, flags), Assert.IsType<RenderViewCreation>(value)),
                value => Assert.Equal(new RenderViewDestruction(renderView), Assert.IsType<RenderViewDestruction>(value)));
        }
        finally
        {
            backend.DestroyTexture(texture);
            backend.DestroyGpuHeap(heap);
        }
    }

    [Fact]
    public void ConsumerWritesMixedResourcesToCallerSelectedDescriptorSlots()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeDescriptors: observed.Add);
        (NativeGpuHeap heap, NativeGpuTextureHandle texture) = CreateTextureForViews(backend);
        NativeGpuLinearRegion region = backend.CreateLinearRegion(1ul << 42, heap, 0);
        var data = new NativeGpuRange(region, (1ul << 40) + 128, (1ul << 39) + 64);
        var view = new NativeGpuTextureView(texture, NativeGpuTextureViewDimension.TwoDArray,
            GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Stencil, 1, 3, 2, 4);
        const uint capacity = (1u << 31) + 4;
        const uint slot = (1u << 31) + 2;
        NativeGpuDescriptorHeap descriptors = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, capacity);

        try
        {
            backend.WriteTextureDescriptor(descriptors, slot, view, NativeGpuTextureDescriptorType.Sampled);
            backend.WriteTextureDescriptor(descriptors, slot, view, NativeGpuTextureDescriptorType.Storage);
            backend.WriteBufferDescriptor(descriptors, slot + 1, data, NativeGpuBufferAccess.ReadOnly);
            backend.WriteBufferDescriptor(descriptors, slot + 1, data, NativeGpuBufferAccess.ReadWrite);

            Assert.Equal((NativeGpuDescriptorHeapKind.Resource, capacity), (descriptors.Kind, descriptors.Capacity));
            Assert.Collection(observed,
                value => Assert.Equal(new DescriptorHeapCreation(NativeGpuDescriptorHeapKind.Resource, capacity),
                    Assert.IsType<DescriptorHeapCreation>(value)),
                value => Assert.Equal(new TextureDescriptorWrite(descriptors, slot, view, NativeGpuTextureDescriptorType.Sampled),
                    Assert.IsType<TextureDescriptorWrite>(value)),
                value => Assert.Equal(new TextureDescriptorWrite(descriptors, slot, view, NativeGpuTextureDescriptorType.Storage),
                    Assert.IsType<TextureDescriptorWrite>(value)),
                value => Assert.Equal(new BufferDescriptorWrite(descriptors, slot + 1, data, NativeGpuBufferAccess.ReadOnly),
                    Assert.IsType<BufferDescriptorWrite>(value)),
                value => Assert.Equal(new BufferDescriptorWrite(descriptors, slot + 1, data, NativeGpuBufferAccess.ReadWrite),
                    Assert.IsType<BufferDescriptorWrite>(value)));
        }
        finally
        {
            backend.DestroyDescriptorHeap(descriptors);
            backend.DestroyLinearRegion(region);
            backend.DestroyTexture(texture);
            backend.DestroyGpuHeap(heap);
        }
    }

    [Fact]
    public void ConsumerPassesEverySamplerValueToItsSelectedSlot()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeDescriptors: observed.Add);
        NativeGpuDescriptorHeap heap = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Sampler, 16);
        var sampler = new NativeGpuSamplerDescription(
            NativeGpuSamplerFilter.Nearest, NativeGpuSamplerFilter.Linear, NativeGpuSamplerFilter.Nearest,
            NativeGpuSamplerAddressMode.MirrorRepeat, NativeGpuSamplerAddressMode.ClampToEdge, NativeGpuSamplerAddressMode.Repeat,
            2.25f, 6.75f, 4.5f, true, GpuCompareOp.GreaterEqual);

        backend.WriteSamplerDescriptor(heap, 7, sampler);
        backend.DestroyDescriptorHeap(heap);

        Assert.Collection(observed,
            value => Assert.Equal(new DescriptorHeapCreation(NativeGpuDescriptorHeapKind.Sampler, 16),
                Assert.IsType<DescriptorHeapCreation>(value)),
            value => Assert.Equal(new SamplerDescriptorWrite(heap, 7, sampler), Assert.IsType<SamplerDescriptorWrite>(value)),
            value => Assert.Equal(new DescriptorHeapDestruction(heap), Assert.IsType<DescriptorHeapDestruction>(value)));
    }

    [Fact]
    public void ConsumerSelectsResourceAndSamplerHeapsIndependently()
    {
        List<object> observed = [];
        using INativeGpuBackend backend = new ExternalBackend(_ => { }, observeCommands: observed.Add);
        NativeGpuDescriptorHeap resources = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 16);
        NativeGpuDescriptorHeap replacement = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 32);
        NativeGpuDescriptorHeap samplers = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Sampler, 8);

        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.SetResourceDescriptorHeap(resources);
            commands.SetSamplerDescriptorHeap(samplers);
            commands.SetResourceDescriptorHeap(replacement);

            Assert.Collection(observed,
                value => Assert.Equal(new ResourceHeapSelection(resources), Assert.IsType<ResourceHeapSelection>(value)),
                value => Assert.Equal(new SamplerHeapSelection(samplers), Assert.IsType<SamplerHeapSelection>(value)),
                value => Assert.Equal(new ResourceHeapSelection(replacement), Assert.IsType<ResourceHeapSelection>(value)));
        }
        finally
        {
            backend.DestroyDescriptorHeap(samplers);
            backend.DestroyDescriptorHeap(replacement);
            backend.DestroyDescriptorHeap(resources);
        }
    }

    private static (NativeGpuHeap Heap, NativeGpuTextureHandle Texture) CreateTextureForViews(INativeGpuBackend backend)
    {
        var description = new NativeGpuTextureDescription(
            NativeGpuTextureDimension.TwoD, 64, 32, 1, 4, 6, 1, GpuFormat.Depth24PlusStencil8,
            NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.DepthStencilAttachment);
        NativeGpuMemoryRequirements requirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = backend.CreateGpuHeap(requirements.Size, requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        return (heap, backend.CreateTexture(description, heap, 0));
    }

    private sealed record RenderViewCreation(NativeGpuTextureView View, NativeGpuRenderViewFlags Flags);
    private sealed record RenderViewDestruction(NativeGpuRenderViewHandle View);
    private sealed record DescriptorHeapCreation(NativeGpuDescriptorHeapKind Kind, uint Capacity);
    private sealed record DescriptorHeapDestruction(NativeGpuDescriptorHeap Heap);
    private sealed record TextureDescriptorWrite(NativeGpuDescriptorHeap Heap, uint Index,
        NativeGpuTextureView View, NativeGpuTextureDescriptorType Type);
    private sealed record BufferDescriptorWrite(NativeGpuDescriptorHeap Heap, uint Index,
        NativeGpuRange Range, NativeGpuBufferAccess Access);
    private sealed record SamplerDescriptorWrite(NativeGpuDescriptorHeap Heap, uint Index, NativeGpuSamplerDescription Description);
    private sealed record ResourceHeapSelection(NativeGpuDescriptorHeap Heap);
    private sealed record SamplerHeapSelection(NativeGpuDescriptorHeap Heap);

    private sealed partial class ExternalBackend
    {
        public NativeGpuRenderViewHandle CreateRenderView(NativeGpuTextureView view,
            NativeGpuRenderViewFlags flags = NativeGpuRenderViewFlags.None)
        {
            observeDescriptors?.Invoke(new RenderViewCreation(view, flags));
            return new RenderView(flags);
        }

        public void DestroyRenderView(NativeGpuRenderViewHandle view)
            => observeDescriptors?.Invoke(new RenderViewDestruction((RenderView)view));

        public NativeGpuDescriptorHeap CreateDescriptorHeap(NativeGpuDescriptorHeapKind kind, uint capacity)
        {
            observeDescriptors?.Invoke(new DescriptorHeapCreation(kind, capacity));
            return new DescriptorHeap(kind, capacity);
        }

        public void DestroyDescriptorHeap(NativeGpuDescriptorHeap heap)
            => observeDescriptors?.Invoke(new DescriptorHeapDestruction((DescriptorHeap)heap));

        public void WriteTextureDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuTextureView view,
            NativeGpuTextureDescriptorType type = NativeGpuTextureDescriptorType.Sampled)
            => observeDescriptors?.Invoke(new TextureDescriptorWrite((DescriptorHeap)heap, index, view, type));

        public void WriteBufferDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuRange range, NativeGpuBufferAccess access)
            => observeDescriptors?.Invoke(new BufferDescriptorWrite((DescriptorHeap)heap, index, range, access));

        public void WriteSamplerDescriptor(NativeGpuDescriptorHeap heap, uint index, NativeGpuSamplerDescription description)
            => observeDescriptors?.Invoke(new SamplerDescriptorWrite((DescriptorHeap)heap, index, description));

        private sealed class RenderView(NativeGpuRenderViewFlags flags) : NativeGpuRenderViewHandle(flags);
        private sealed class DescriptorHeap(NativeGpuDescriptorHeapKind kind, uint capacity) : NativeGpuDescriptorHeap(kind, capacity);
    }
}
