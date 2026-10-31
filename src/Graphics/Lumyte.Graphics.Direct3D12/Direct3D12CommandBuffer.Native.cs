using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe partial class Direct3D12CommandBuffer
{
    internal nint Native { get; private set; }

    internal void EndCompute(Direct3D12ComputeEncoder pass)
    {
        ValidateEnd(pass);
        _active = null;
    }

    internal void EndRender(Direct3D12RenderEncoder pass)
    {
        ValidateEnd(pass);
        _active = null;
    }

    internal void Indirect(nint buffer, ulong offset, uint kind)
    {
        _owner.Check(NativeMethods.Indirect(_owner.Native, Native, buffer, offset, kind, out nint signature));
        KeepBinding(new NativeObject(signature));
    }

    private static NativeTextureRegion Region(TextureCopyRegion region, uint layer) => new()
    {
        Subresource = region.MipLevel + ((region.BaseArrayLayer + layer) * region.Texture.MipLevels),
        X = region.OriginX,
        Y = region.OriginY,
        Width = region.Width,
        Height = region.Height,
    };

    private static uint TextureStateNative(TextureState state) => state switch
    {
        TextureState.Undefined => uint.MaxValue,
        TextureState.Present => 0,
        TextureState.CopySource => 7,
        TextureState.CopyDestination => 8,
        TextureState.Sampled => 6,
        TextureState.ColorAttachment => 2,
        TextureState.DepthStencilAttachment => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static NativeAttachment Attachment(IGraphicsTextureView view, uint clear, float r, float g, float b, float a) => new()
    {
        Resource = ((Direct3D12TextureView)view).Native,
        Format = Direct3D12Texture.NativeFormat(view.Info.Format),
        Mip = view.Info.BaseMipLevel,
        Layer = view.Info.BaseArrayLayer,
        Clear = clear,
        R = r,
        G = g,
        B = b,
        A = a,
    };

    private void Initialize()
    {
        _owner.Check(NativeMethods.CommandsCreate(_owner.Native, out nint native));
        Native = native;
    }

    private void FinishNative() => _owner.Check(NativeMethods.CommandsFinish(Native));

    private void DisposeNative() => NativeMethods.CommandsDestroy(Native);

    private void CopyBufferNative<TSource, TDestination>(Direct3D12Buffer<TSource> source, ulong sourceOffset, Direct3D12Buffer<TDestination> destination, ulong destinationOffset, ulong size)
        where TSource : unmanaged
        where TDestination : unmanaged
        => NativeMethods.CommandsCopy(Native, source.Native, sourceOffset, destination.Native, destinationOffset, size);

    private void CopyTextureNative(TextureCopyRegion source, TextureCopyRegion destination)
    {
        for (uint layer = 0; layer < source.ArrayLayerCount; layer++)
        {
            NativeMethods.CopyTexture(Native, ((Direct3D12Texture)source.Texture).Native, Region(source, layer), ((Direct3D12Texture)destination.Texture).Native, Region(destination, layer));
        }
    }

    private void CopyBufferTextureNative(BufferTextureCopyLayout buffer, TextureCopyRegion region, bool upload)
    {
        for (uint layer = 0; layer < region.ArrayLayerCount; layer++)
        {
            ulong offset = checked(buffer.Buffer.OffsetInBytes + ((ulong)layer * buffer.RowsPerImage * buffer.BytesPerRow));
            if (offset % 512 != 0)
            {
                throw new ArgumentException("Each image footprint must begin at a 512-byte-aligned offset.");
            }
        }

        for (uint layer = 0; layer < region.ArrayLayerCount; layer++)
        {
            NativeMethods.CopyBufferTexture(Native, ((Direct3D12Buffer<byte>)buffer.Buffer.Buffer).Native, checked(buffer.Buffer.OffsetInBytes + ((ulong)layer * buffer.RowsPerImage * buffer.BytesPerRow)), buffer.BytesPerRow, ((Direct3D12Texture)region.Texture).Native, Region(region, layer), upload ? 1 : 0);
        }
    }

    private void TextureBarrierNative(Direct3D12Texture texture, TextureBarrierDesc barrier)
    {
        TextureSubresourceRange range = barrier.Range;
        NativeMethods.TextureDependency(Native, texture.Native, range.BaseMipLevel, range.MipLevelCount, range.BaseArrayLayer, range.ArrayLayerCount, TextureStateNative(barrier.BeforeState), TextureStateNative(barrier.AfterState), (uint)barrier.Before.Stages, (uint)barrier.Before.Access, (uint)barrier.After.Stages, (uint)barrier.After.Access);
    }

    private void MemoryBarrierNative(MemoryBarrierDesc barrier) => NativeMethods.GlobalDependency(Native, (uint)barrier.Before.Stages, (uint)barrier.Before.Access, (uint)barrier.After.Stages, (uint)barrier.After.Access);

    private void BufferBarrierNative<T>(Direct3D12Buffer<T> buffer, BufferBarrierDesc<T> barrier)
        where T : unmanaged
        => NativeMethods.BufferDependency(Native, buffer.Native, barrier.Buffer.OffsetInBytes, barrier.Buffer.SizeInBytes, (uint)barrier.Before.Stages, (uint)barrier.Before.Access, (uint)barrier.After.Stages, (uint)barrier.After.Access);

    private IComputeEncoder BeginComputeNative() => new Direct3D12ComputeEncoder(this);

    private IRenderEncoder BeginRenderNative(RenderColorAttachmentDesc[] attachments, RenderDepthStencilAttachmentDesc? depth)
    {
        NativeAttachment[] colors = attachments.Select(a => Attachment(a.View, a.LoadOp == AttachmentLoadOp.Clear ? 1u : 0, (float)a.ClearValue.Red, (float)a.ClearValue.Green, (float)a.ClearValue.Blue, (float)a.ClearValue.Alpha)).ToArray();
        NativeAttachment d = depth == null ? default : Attachment(depth.View, (depth.DepthLoadOp == AttachmentLoadOp.Clear ? 1u : 0) | (depth.View.Info.Format == TextureFormat.Depth24Stencil8 && depth.StencilLoadOp == AttachmentLoadOp.Clear ? 2u : 0), depth.DepthClearValue, depth.StencilClearValue, 0, 0);
        try
        {
            fixed (NativeAttachment* c = colors)
            {
                _owner.Check(NativeMethods.RenderBegin(_owner.Native, Native, c, (uint)colors.Length, depth == null ? null : &d));
            }
        }
        finally
        {
            NativeMethods.DetachHeaps(Native, out nint rtv, out nint dsv);
            if (rtv != 0)
            {
                KeepBinding(new NativeObject(rtv));
            }

            if (dsv != 0)
            {
                KeepBinding(new NativeObject(dsv));
            }
        }

        return new Direct3D12RenderEncoder(this, attachments, depth);
    }
}
