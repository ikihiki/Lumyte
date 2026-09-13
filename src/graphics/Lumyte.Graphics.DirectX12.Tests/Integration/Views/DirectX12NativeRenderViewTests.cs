using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
[Trait("Category", "DirectX12Conformance")]
public sealed class DirectX12NativeRenderViewTests
{
    [Theory]
    [InlineData(NativeGpuTextureDimension.OneD, NativeGpuTextureViewDimension.OneD, 1u)]
    [InlineData(NativeGpuTextureDimension.TwoD, NativeGpuTextureViewDimension.TwoD, 1u)]
    [InlineData(NativeGpuTextureDimension.TwoD, NativeGpuTextureViewDimension.TwoDArray, 1u)]
    [InlineData(NativeGpuTextureDimension.TwoD, NativeGpuTextureViewDimension.TwoD, 4u)]
    [InlineData(NativeGpuTextureDimension.TwoD, NativeGpuTextureViewDimension.TwoDArray, 4u)]
    [InlineData(NativeGpuTextureDimension.ThreeD, NativeGpuTextureViewDimension.ThreeD, 1u)]
    [InlineData(NativeGpuTextureDimension.ThreeD, NativeGpuTextureViewDimension.TwoD, 1u)]
    [InlineData(NativeGpuTextureDimension.ThreeD, NativeGpuTextureViewDimension.TwoDArray, 1u)]
    public void ColorAttachmentDimensionsCanBeCreatedAndReleased(NativeGpuTextureDimension dimension,
        NativeGpuTextureViewDimension viewDimension, uint samples)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        bool volume = dimension == NativeGpuTextureDimension.ThreeD;
        bool array = viewDimension == NativeGpuTextureViewDimension.TwoDArray;
        NativeGpuTextureDescription description = Description with
        {
            Dimension = dimension, Height = dimension == NativeGpuTextureDimension.OneD ? 1u : 32u,
            Depth = volume ? 8u : 1u, LayerCount = array && !volume ? 4u : 1u,
            MipCount = samples > 1 ? 1u : 3u, SampleCount = samples,
        };
        WithTexture(backend, description, texture =>
        {
            bool slice = volume && viewDimension != NativeGpuTextureViewDimension.ThreeD;
            var view = new NativeGpuTextureView(texture, viewDimension, description.Format, NativeGpuTextureAspect.Color,
                samples > 1 ? 0u : 1u, 1, slice || array ? 1u : 0u, array ? 2u : 1u);

            NativeGpuRenderViewHandle render = backend.CreateRenderView(view);
            backend.DestroyRenderView(render);

            Assert.Throws<ObjectDisposedException>(() => backend.DestroyRenderView(render));
        });
    }

    [Theory]
    [InlineData(GpuFormat.D32Float, NativeGpuTextureAspect.Depth, NativeGpuRenderViewFlags.DepthReadOnly, 1u)]
    [InlineData(GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.DepthStencil, NativeGpuRenderViewFlags.None, 1u)]
    [InlineData(GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.DepthStencil, NativeGpuRenderViewFlags.StencilReadOnly, 1u)]
    [InlineData(GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.DepthStencil, NativeGpuRenderViewFlags.DepthReadOnly | NativeGpuRenderViewFlags.StencilReadOnly, 4u)]
    public void DepthStencilAttachmentsRetainReadOnlyFlags(GpuFormat format, NativeGpuTextureAspect aspect,
        NativeGpuRenderViewFlags flags, uint samples)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuTextureDescription description = Description with
        {
            Format = format, Usage = NativeGpuTextureUsage.DepthStencilAttachment, SampleCount = samples, LayerCount = 3,
        };
        WithTexture(backend, description, texture =>
        {
            var view = new NativeGpuTextureView(texture, NativeGpuTextureViewDimension.TwoDArray, format, aspect, 0, 1, 1, 2);

            NativeGpuRenderViewHandle render = backend.CreateRenderView(view, flags);
            try { Assert.Equal(flags, render.Flags); }
            finally { backend.DestroyRenderView(render); }
        });
    }

    [Fact]
    public void DestroyingRenderViewLeavesItsTextureAvailable()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        WithTexture(backend, Description, texture =>
        {
            var view = new NativeGpuTextureView(texture, NativeGpuTextureViewDimension.TwoD,
                GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 0, 1, 0, 1);
            NativeGpuRenderViewHandle original = backend.CreateRenderView(view);
            backend.DestroyRenderView(original);

            NativeGpuRenderViewHandle replacement = backend.CreateRenderView(view);
            try { Assert.NotSame(original, replacement); }
            finally { backend.DestroyRenderView(replacement); }
        });
    }

    [Fact]
    public void ForeignViewDestructionLeavesTheOwnersViewAvailable()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using DirectX12Backend other = DirectX12Backend.Create();
        WithTexture(backend, Description, texture =>
        {
            var view = new NativeGpuTextureView(texture, NativeGpuTextureViewDimension.TwoD,
                GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 0, 1, 0, 1);
            NativeGpuRenderViewHandle render = backend.CreateRenderView(view);
            try
            {
                ArgumentException error = Assert.Throws<ArgumentException>(() => other.DestroyRenderView(render));

                Assert.Equal("view", error.ParamName);
            }
            finally { backend.DestroyRenderView(render); }
        });
    }

    [Theory]
    [InlineData(NativeGpuTextureViewDimension.TwoD, 0u, 1u)]
    [InlineData(NativeGpuTextureViewDimension.TwoDArray, 1u, 2u)]
    public void SliceAttachmentViewCannotBeUsedForWholeVolumeTransitions(NativeGpuTextureViewDimension dimension, uint first, uint count)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        WithTexture(backend, Description with { Dimension = NativeGpuTextureDimension.ThreeD, Depth = 4 }, texture =>
        {
            var view = new NativeGpuTextureView(texture, dimension, GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color,
                0, 1, first, count);
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();

            ArgumentException error = Assert.Throws<ArgumentException>(() => commands.DiscardTexture(view, GpuTextureLayout.General));

            Assert.Equal("view", error.ParamName);
        });
    }

    [Fact]
    public void WholeVolumeViewCanBeTransitioned()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        WithTexture(backend, Description with { Dimension = NativeGpuTextureDimension.ThreeD, Depth = 4 }, texture =>
        {
            var view = new NativeGpuTextureView(texture, NativeGpuTextureViewDimension.ThreeD,
                GpuFormat.Rgba8Unorm, NativeGpuTextureAspect.Color, 0, 1, 0, 1);
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            using NativeGpuSemaphore completion = backend.CreateSemaphore(0);
            commands.DiscardTexture(view, GpuTextureLayout.ColorAttachment);
            commands.TextureTransition(view, GpuTextureLayout.ColorAttachment, GpuTextureLayout.General);

            backend.MainQueue.Submit([commands], new(completion, 1));
            completion.WaitCpu(1);

            Assert.True(completion.IsComplete(1));
        });
    }

    private static void WithTexture(DirectX12Backend backend, NativeGpuTextureDescription description,
        Action<NativeGpuTextureHandle> action)
    {
        NativeGpuMemoryRequirements requirements = backend.GetTextureMemoryRequirements(description, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = backend.CreateGpuHeap(requirements.Size, requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        try
        {
            NativeGpuTextureHandle texture = backend.CreateTexture(description, heap, 0);
            try { action(texture); }
            finally { backend.DestroyTexture(texture); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    private static NativeGpuTextureDescription Description => new(NativeGpuTextureDimension.TwoD,
        32, 32, 1, 1, 1, 1, GpuFormat.Rgba8Unorm, NativeGpuTextureUsage.ColorAttachment);
}
