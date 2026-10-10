using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed partial class BrowserCommandBuffer
{
    private JSObject? _encoder;
    private JSObject? _command;

    internal JSObject Native => _command ?? throw new InvalidOperationException("Command is not finished.");

    internal void EndRender(BrowserRenderEncoder pass, JSObject handle)
    {
        ValidateEnd(pass);
        BrowserInterop.EndRenderPass(handle);
        handle.Dispose();
        _active = null;
    }

    internal void EndCompute(BrowserComputeEncoder pass, JSObject handle)
    {
        ValidateEnd(pass);
        BrowserInterop.EndComputePass(handle);
        handle.Dispose();
        _active = null;
    }

    private static string RegionJson(TextureCopyRegion r) => FormattableString.Invariant($"{{\"mip\":{r.MipLevel},\"x\":{r.OriginX},\"y\":{r.OriginY},\"layer\":{r.BaseArrayLayer},\"width\":{r.Width},\"height\":{r.Height},\"layers\":{r.ArrayLayerCount}}}");

    private void Initialize() => _encoder = BrowserInterop.CreateCommandEncoder(_owner.Handle);

    private void CopyBufferNative<TSource, TDestination>(BrowserBuffer<TSource> source, ulong sourceOffset, BrowserBuffer<TDestination> destination, ulong destinationOffset, ulong size)
        where TSource : unmanaged
        where TDestination : unmanaged
        => BrowserInterop.RecordBufferCopy(_encoder!, source.Native, sourceOffset, destination.Native, destinationOffset, size);

    private void CopyTextureNative(TextureCopyRegion source, TextureCopyRegion destination)
        => BrowserInterop.RecordTextureCopy(_encoder!, ((BrowserTexture)source.Texture).Native, RegionJson(source), ((BrowserTexture)destination.Texture).Native, RegionJson(destination));

    private void CopyBufferTextureNative(BufferTextureCopyLayout buffer, TextureCopyRegion region, bool upload)
        => BrowserInterop.RecordBufferTextureCopy(_encoder!, ((BrowserBuffer<byte>)buffer.Buffer.Buffer).Native, buffer.Buffer.OffsetInBytes, buffer.BytesPerRow, buffer.RowsPerImage, ((BrowserTexture)region.Texture).Native, RegionJson(region), upload);

    private void MemoryBarrierNative(MemoryBarrierDesc barrier)
    {
        // WebGPU handles physical memory visibility for ordered usages.
    }

    private void BufferBarrierNative<T>(BrowserBuffer<T> buffer, BufferBarrierDesc<T> barrier)
        where T : unmanaged
    {
        // WebGPU provides visibility for ordered buffer usages.
    }

    private void TextureBarrierNative(BrowserTexture texture, TextureBarrierDesc barrier)
    {
        // WebGPU owns physical image transitions.
    }

    private IRenderEncoder BeginRenderNative(RenderColorAttachmentDesc[] attachments, RenderDepthStencilAttachmentDesc? depth)
    {
        using JSObject desc = BrowserInterop.CreateRenderDescriptor();
        foreach (RenderColorAttachmentDesc a in attachments)
        {
            BrowserInterop.AddColorAttachment(desc, ((BrowserTextureView)a.View).Native, (int)a.LoadOp, (int)a.StoreOp, a.ClearValue.Red, a.ClearValue.Green, a.ClearValue.Blue, a.ClearValue.Alpha);
        }

        if (depth != null)
        {
            BrowserInterop.AddDepthStencilAttachment(desc, ((BrowserTextureView)depth.View).Native, (int)depth.DepthLoadOp, (int)depth.DepthStoreOp, depth.DepthClearValue, depth.View.Info.Format == TextureFormat.Depth24Stencil8, (int)depth.StencilLoadOp, (int)depth.StencilStoreOp, (int)depth.StencilClearValue);
        }

        return new BrowserRenderEncoder(this, BrowserInterop.BeginRenderPass(_encoder!, desc), attachments, depth);
    }

    private IComputeEncoder BeginComputeNative() => new BrowserComputeEncoder(this, BrowserInterop.BeginComputePass(_encoder!));

    private void FinishNative()
    {
        try
        {
            _command = BrowserInterop.FinishCommands(_encoder!);
            _encoder!.Dispose();
            _encoder = null;
        }
        catch
        {
            State = CommandBufferState.Faulted;
            throw;
        }
    }

    private void DisposeNative()
    {
        _encoder?.Dispose();
        _command?.Dispose();
    }
}
