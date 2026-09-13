using Lumyte.Graphics.Native;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace Lumyte.Graphics.DirectX12;

public sealed unsafe partial class DirectX12Backend
{
    internal static BarrierSync BarrierStages(GpuStage stages)
    {
        const GpuStage known = GpuStage.DrawIndirect | GpuStage.IndexInput | GpuStage.VertexShader
            | GpuStage.AmplificationShader | GpuStage.MeshShader | GpuStage.PixelShader | GpuStage.ComputeShader
            | GpuStage.ColorOutput | GpuStage.DepthStencil | GpuStage.Copy | GpuStage.AllGraphics | GpuStage.All | GpuStage.Host;
        if ((stages & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(stages)); }
        // D3D12 has no host pipeline stage. The explicit global barrier covers all GPU stages;
        // completing a caller fence with Wait establishes when mapped CPU access may start.
        if ((stages & (GpuStage.All | GpuStage.Host)) != 0) { return BarrierSync.All; }
        BarrierSync result = BarrierSync.None;
        if ((stages & GpuStage.AllGraphics) != 0) { result |= BarrierSync.Draw; }
        if ((stages & GpuStage.DrawIndirect) != 0) { result |= BarrierSync.ExecuteIndirect; }
        if ((stages & GpuStage.IndexInput) != 0) { result |= BarrierSync.IndexInput; }
        if ((stages & (GpuStage.VertexShader | GpuStage.AmplificationShader | GpuStage.MeshShader)) != 0) { result |= BarrierSync.VertexShading; }
        if ((stages & GpuStage.PixelShader) != 0) { result |= BarrierSync.PixelShading; }
        if ((stages & GpuStage.ComputeShader) != 0) { result |= BarrierSync.ComputeShading; }
        if ((stages & GpuStage.ColorOutput) != 0) { result |= BarrierSync.RenderTarget; }
        if ((stages & GpuStage.DepthStencil) != 0) { result |= BarrierSync.DepthStencil; }
        if ((stages & GpuStage.Copy) != 0) { result |= BarrierSync.Copy; }
        return result;
    }

    internal static BarrierAccess BarrierAccesses(GpuAccess access)
    {
        const GpuAccess known = GpuAccess.ShaderRead | GpuAccess.ShaderWrite | GpuAccess.DescriptorRead
            | GpuAccess.ColorRead | GpuAccess.ColorWrite | GpuAccess.DepthStencilRead | GpuAccess.DepthStencilWrite
            | GpuAccess.CopyRead | GpuAccess.CopyWrite | GpuAccess.IndexRead | GpuAccess.IndirectRead
            | GpuAccess.HostRead | GpuAccess.HostWrite;
        if ((access & ~known) != 0) { throw new ArgumentOutOfRangeException(nameof(access)); }
        if ((access & GpuAccess.HostRead) != 0) { return BarrierAccess.Common; }
        // Host writes precede ExecuteCommandLists, whose start guarantees coherent GPU caches.
        // They are not GPU writes to flush: COMMON in AccessBefore would instead declare every
        // GPU write type, and a COMMON -> COPY_SOURCE global barrier is rejected by the runtime.
        // Keep any explicitly combined GPU accesses; pure HostWrite maps to NO_ACCESS below.
        BarrierAccess result = 0;
        if ((access & GpuAccess.ShaderRead) != 0) { result |= BarrierAccess.ShaderResource | BarrierAccess.ConstantBuffer | BarrierAccess.UnorderedAccess; }
        if ((access & GpuAccess.ShaderWrite) != 0) { result |= BarrierAccess.UnorderedAccess; }
        if ((access & (GpuAccess.ColorRead | GpuAccess.ColorWrite)) != 0) { result |= BarrierAccess.RenderTarget; }
        if ((access & GpuAccess.DepthStencilRead) != 0) { result |= BarrierAccess.DepthStencilRead; }
        if ((access & GpuAccess.DepthStencilWrite) != 0) { result |= BarrierAccess.DepthStencilWrite; }
        if ((access & GpuAccess.CopyRead) != 0) { result |= BarrierAccess.CopySource; }
        if ((access & GpuAccess.CopyWrite) != 0) { result |= BarrierAccess.CopyDest; }
        if ((access & GpuAccess.IndexRead) != 0) { result |= BarrierAccess.IndexBuffer; }
        if ((access & GpuAccess.IndirectRead) != 0) { result |= BarrierAccess.IndirectArgument; }
        // D3D12 descriptor heaps are opaque CPU-written objects, not resources in a barrier access scope.
        return result == 0 ? BarrierAccess.NoAccess : result;
    }

    private static void EncodeBarrier(ComPtr<ID3D12GraphicsCommandList8> commands, GlobalBarrier barrier)
    {
        var group = new BarrierGroup { Type = BarrierType.Global, NumBarriers = 1, Anonymous = new() { PGlobalBarriers = &barrier } };
        commands.Barrier(1, &group);
    }

    private void EncodeTransition(ComPtr<ID3D12GraphicsCommandList8> commands, NativeGpuTextureView view,
        GpuTextureLayout before, GpuTextureLayout after)
    {
        TextureRecord texture = RequireTexture(view.Texture);
        (uint plane, uint count) = TexturePlanes(texture.Description.Format, view.Aspect);
        var barrier = new TextureBarrier
        {
            SyncBefore = BarrierSync.All,
            SyncAfter = BarrierSync.All,
            AccessBefore = LayoutAccess(before),
            AccessAfter = LayoutAccess(after),
            LayoutBefore = TextureLayoutFor(before),
            LayoutAfter = TextureLayoutFor(after),
            PResource = texture.Resource.Handle,
            Subresources = new(view.BaseMip, view.MipCount, view.BaseLayer, view.LayerCount, plane, count),
            Flags = before == GpuTextureLayout.Undefined ? TextureBarrierFlags.Discard : TextureBarrierFlags.None,
        };
        var group = new BarrierGroup { Type = BarrierType.Texture, NumBarriers = 1, Anonymous = new() { PTextureBarriers = &barrier } };
        commands.Barrier(1, &group);
    }

    internal static void RequireTransitionView(NativeGpuTextureView view, NativeGpuTextureDimension dimension)
    {
        // A 3D barrier covers a whole mip, not the depth slices selected by an attachment view.
        if (dimension == NativeGpuTextureDimension.ThreeD
            && (view.Dimension != NativeGpuTextureViewDimension.ThreeD || view.BaseLayer != 0 || view.LayerCount != 1))
        {
            throw new ArgumentException("A 3D texture transition requires a ThreeD view with the whole mip depth.", nameof(view));
        }
    }

    internal static (uint First, uint Count) TexturePlanes(GpuFormat format, NativeGpuTextureAspect aspect)
    {
        // Color and depth both become plane zero. Validate the distinction while the caller's
        // aspect is still available, before that information disappears from native arguments.
        return (format, aspect) switch
        {
            (GpuFormat.D32Float or GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Depth) => (0, 1),
            (GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.Stencil) => (1, 1),
            (GpuFormat.Depth24PlusStencil8, NativeGpuTextureAspect.DepthStencil) => (0, 2),
            (GpuFormat.Rgba8Unorm or GpuFormat.Rgba8UnormSrgb or GpuFormat.Bgra8Unorm or GpuFormat.Bgra8UnormSrgb
                or GpuFormat.R32Float or GpuFormat.R8Unorm or GpuFormat.Rg8Unorm, NativeGpuTextureAspect.Color) => (0, 1),
            _ => throw new ArgumentException("The aspect does not represent a plane of the texture format.", nameof(aspect)),
        };
    }

    private static BarrierLayout TextureLayoutFor(GpuTextureLayout layout) => layout switch
    {
        GpuTextureLayout.Undefined => BarrierLayout.Undefined,
        GpuTextureLayout.Common => BarrierLayout.Common,
        GpuTextureLayout.General => BarrierLayout.DirectQueueCommon,
        GpuTextureLayout.ShaderRead => BarrierLayout.DirectQueueShaderResource,
        GpuTextureLayout.ColorAttachment => BarrierLayout.RenderTarget,
        GpuTextureLayout.DepthStencilRead => BarrierLayout.DepthStencilRead,
        GpuTextureLayout.DepthStencilWrite => BarrierLayout.DepthStencilWrite,
        GpuTextureLayout.CopySource => BarrierLayout.DirectQueueCopySource,
        GpuTextureLayout.CopyDestination => BarrierLayout.DirectQueueCopyDest,
        GpuTextureLayout.Present => BarrierLayout.Present,
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };

    private static BarrierAccess LayoutAccess(GpuTextureLayout layout) => layout switch
    {
        GpuTextureLayout.Undefined => BarrierAccess.NoAccess,
        GpuTextureLayout.Common or GpuTextureLayout.General or GpuTextureLayout.Present => BarrierAccess.Common,
        GpuTextureLayout.ShaderRead => BarrierAccess.ShaderResource,
        GpuTextureLayout.ColorAttachment => BarrierAccess.RenderTarget,
        GpuTextureLayout.DepthStencilRead => BarrierAccess.DepthStencilRead,
        GpuTextureLayout.DepthStencilWrite => BarrierAccess.DepthStencilWrite,
        GpuTextureLayout.CopySource => BarrierAccess.CopySource,
        GpuTextureLayout.CopyDestination => BarrierAccess.CopyDest,
        _ => throw new ArgumentOutOfRangeException(nameof(layout)),
    };
}
