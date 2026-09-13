using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
public sealed class DirectX12NativeDescriptorStorageTests
{
    [Theory]
    [InlineData(NativeGpuDescriptorHeapKind.Resource)]
    [InlineData(NativeGpuDescriptorHeapKind.Sampler)]
    public void DescriptorStorageRetainsCallerCapacityAndRejectsRepeatedDestruction(NativeGpuDescriptorHeapKind kind)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuDescriptorHeap heap = backend.CreateDescriptorHeap(kind, 19);

        Assert.Equal((kind, 19u), (heap.Kind, heap.Capacity));
        backend.DestroyDescriptorHeap(heap);

        Assert.Throws<ObjectDisposedException>(() => backend.DestroyDescriptorHeap(heap));
    }

    [Fact]
    public void CallerSlotsCanMixAndReplaceTextureAndBufferDescriptors()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using var texture = new TextureAllocation(backend, TextureDescription);
        NativeGpuMemoryRequirements requirements = backend.GetLinearMemoryRequirements(4096, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap backing = backend.CreateGpuHeap(requirements.Size * 2, requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        try
        {
            NativeGpuLinearRegion region = backend.CreateLinearRegion(4096, backing, requirements.Size);
            try
            {
                NativeGpuDescriptorHeap heap = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 5);
                try
                {
                    backend.WriteTextureDescriptor(heap, 4, texture.View);
                    backend.WriteBufferDescriptor(heap, 1, new(region, 512, 1024), NativeGpuBufferAccess.ReadOnly);
                    backend.WriteBufferDescriptor(heap, 2, new(region, 1024, 512), NativeGpuBufferAccess.ReadWrite);
                    backend.WriteTextureDescriptor(heap, 3, texture.View, NativeGpuTextureDescriptorType.Storage);
                    backend.WriteBufferDescriptor(heap, 4, new(region, 512, 1024), NativeGpuBufferAccess.ReadOnly);
                    backend.WriteTextureDescriptor(heap, 1, texture.View);

                    SubmitHeaps(backend, heap);
                }
                finally { backend.DestroyDescriptorHeap(heap); }
            }
            finally { backend.DestroyLinearRegion(region); }
        }
        finally { backend.DestroyGpuHeap(backing); }
    }

    [Fact]
    public void ResourceAndSamplerSelectionsCanBeChangedIndependently()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuDescriptorHeap resource = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 8);
        NativeGpuDescriptorHeap replacement = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 4);
        NativeGpuDescriptorHeap sampler = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Sampler, 3);
        try
        {
            backend.WriteSamplerDescriptor(sampler, 2, new(MaxAnisotropy: 8, CompareEnabled: true));
            backend.WriteSamplerDescriptor(sampler, 0, new(MinFilter: NativeGpuSamplerFilter.Nearest));
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            using NativeGpuSemaphore completion = backend.CreateSemaphore(0);
            commands.SetSamplerDescriptorHeap(sampler);
            commands.SetResourceDescriptorHeap(resource);
            commands.SetResourceDescriptorHeap(replacement);
            commands.SetSamplerDescriptorHeap(sampler);

            backend.MainQueue.Submit([commands], new(completion, 1));
            commands.Dispose();
            completion.WaitCpu(1);

            Assert.True(completion.IsComplete(1));
        }
        finally
        {
            backend.DestroyDescriptorHeap(sampler);
            backend.DestroyDescriptorHeap(replacement);
            backend.DestroyDescriptorHeap(resource);
        }
    }

    [Fact]
    public void OutOfRangeWriteLeavesTheHeapUsable()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuDescriptorHeap heap = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Sampler, 2);
        try
        {
            ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
                backend.WriteSamplerDescriptor(heap, 2, new()));
            backend.WriteSamplerDescriptor(heap, 1, new());

            Assert.Equal("index", error.ParamName);
        }
        finally { backend.DestroyDescriptorHeap(heap); }
    }

    [Fact]
    public void ForeignHeapSelectionLeavesTheOwnersHeapWritable()
    {
        using DirectX12Backend owner = DirectX12Backend.Create();
        using DirectX12Backend other = DirectX12Backend.Create();
        NativeGpuDescriptorHeap heap = owner.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Sampler, 1);
        try
        {
            using NativeGpuCommandBuffer commands = other.MainQueue.StartCommandRecording();

            ArgumentException error = Assert.Throws<ArgumentException>(() => commands.SetSamplerDescriptorHeap(heap));

            Assert.Equal("heap", error.ParamName);
            owner.WriteSamplerDescriptor(heap, 0, new());
        }
        finally { owner.DestroyDescriptorHeap(heap); }
    }

    [Fact]
    public void WrongHeapKindCannotBeSelected()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuDescriptorHeap heap = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Sampler, 1);
        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();

            ArgumentException error = Assert.Throws<ArgumentException>(() => commands.SetResourceDescriptorHeap(heap));

            Assert.Equal("heap", error.ParamName);
        }
        finally { backend.DestroyDescriptorHeap(heap); }
    }

    [Fact]
    public void DestroyedSelectedHeapRejectsSubmissionBeforeQueueAcceptance()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuDescriptorHeap heap = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 1);
        using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.CreateSemaphore(0);
        commands.SetResourceDescriptorHeap(heap);
        backend.DestroyDescriptorHeap(heap);

        Assert.Throws<ObjectDisposedException>(() => backend.MainQueue.Submit([commands], new(completion, 1)));
    }

    [Theory]
    [InlineData(NativeGpuTextureViewDimension.OneD)]
    [InlineData(NativeGpuTextureViewDimension.TwoDArray)]
    [InlineData(NativeGpuTextureViewDimension.ThreeD)]
    [InlineData(NativeGpuTextureViewDimension.Cube)]
    [InlineData(NativeGpuTextureViewDimension.CubeArray)]
    public void TextureDescriptorDimensionsCanBeWritten(NativeGpuTextureViewDimension dimension)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        bool volume = dimension == NativeGpuTextureViewDimension.ThreeD;
        bool one = dimension == NativeGpuTextureViewDimension.OneD;
        bool cube = dimension is NativeGpuTextureViewDimension.Cube or NativeGpuTextureViewDimension.CubeArray;
        uint layers = volume || one ? 1u : 12u;
        using var texture = new TextureAllocation(backend, TextureDescription with
        {
            Dimension = volume ? NativeGpuTextureDimension.ThreeD : one ? NativeGpuTextureDimension.OneD : NativeGpuTextureDimension.TwoD,
            Height = one ? 1u : 32u, Depth = volume ? 8u : 1u, LayerCount = layers, MipCount = 3,
        });
        NativeGpuDescriptorHeap heap = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 2);
        try
        {
            NativeGpuTextureView view = texture.View with { Dimension = dimension, BaseMip = 1,
                MipCount = 2, LayerCount = dimension == NativeGpuTextureViewDimension.Cube ? 6 : layers };
            backend.WriteTextureDescriptor(heap, 1, view);
            if (!cube) { backend.WriteTextureDescriptor(heap, 0, view with { MipCount = 1 }, NativeGpuTextureDescriptorType.Storage); }

            SubmitHeaps(backend, heap);
        }
        finally { backend.DestroyDescriptorHeap(heap); }
    }

    [Theory]
    [InlineData(GpuFormat.D32Float, NativeGpuTextureAspect.Depth)]
    [InlineData(GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Depth)]
    [InlineData(GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Stencil)]
    public void DepthAndStencilShaderDescriptorsCanBeWritten(GpuFormat format, NativeGpuTextureAspect aspect)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using var texture = new TextureAllocation(backend, TextureDescription with
        {
            Format = format, Usage = NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.DepthStencilAttachment,
        });
        NativeGpuDescriptorHeap heap = backend.CreateDescriptorHeap(NativeGpuDescriptorHeapKind.Resource, 1);
        try
        {
            backend.WriteTextureDescriptor(heap, 0, texture.View with { Aspect = aspect });

            SubmitHeaps(backend, heap);
        }
        finally { backend.DestroyDescriptorHeap(heap); }
    }

    private static void SubmitHeaps(DirectX12Backend backend, NativeGpuDescriptorHeap heap)
    {
        using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.CreateSemaphore(0);
        commands.SetResourceDescriptorHeap(heap);
        backend.MainQueue.Submit([commands], new(completion, 1));
        completion.WaitCpu(1);
        Assert.True(completion.IsComplete(1));
    }

    private static NativeGpuTextureDescription TextureDescription => new(NativeGpuTextureDimension.TwoD,
        32, 32, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.Sampled | NativeGpuTextureUsage.Storage);

    private sealed class TextureAllocation : IDisposable
    {
        private readonly DirectX12Backend backend;
        private readonly NativeGpuHeap heap;
        private readonly NativeGpuTextureHandle texture;
        public NativeGpuTextureView View { get; }

        public TextureAllocation(DirectX12Backend backend, NativeGpuTextureDescription description)
        {
            this.backend = backend;
            NativeGpuMemoryRequirements requirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
            heap = backend.CreateGpuHeap(requirements.Size, requirements.Alignment, NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
            try { texture = backend.CreateTexture(description, heap, 0); }
            catch { backend.DestroyGpuHeap(heap); throw; }
            View = new(texture, NativeGpuTextureViewDimension.TwoD, description.Format, NativeGpuTextureAspect.Color, 0, 1, 0, 1);
        }

        public void Dispose() { backend.DestroyTexture(texture); backend.DestroyGpuHeap(heap); }
    }
}
