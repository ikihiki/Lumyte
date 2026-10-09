using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe partial class WgpuCommandBuffer
{
    private WGPUCommandEncoderImpl* _encoder;
    private WGPUCommandBufferImpl* _command;

    internal WGPUCommandBufferImpl* Native => _command;

    internal void EndRender(WgpuRenderEncoder pass, WGPURenderPassEncoderImpl* handle)
    {
        ValidateEnd(pass);
        WGPU.wgpuRenderPassEncoderEnd(handle);
        WGPU.wgpuRenderPassEncoderRelease(handle);
        _active = null;
    }

    internal void EndCompute(WgpuComputeEncoder pass, WGPUComputePassEncoderImpl* handle)
    {
        ValidateEnd(pass);
        WGPU.wgpuComputePassEncoderEnd(handle);
        WGPU.wgpuComputePassEncoderRelease(handle);
        _active = null;
    }

    private static WGPUTexelCopyTextureInfo TextureInfo(TextureCopyRegion region) => new()
    {
        texture = ((WgpuTexture)region.Texture).Native.Handle,
        mipLevel = region.MipLevel,
        origin = new() { x = region.OriginX, y = region.OriginY, z = region.BaseArrayLayer },
        aspect = WGPUTextureAspect.All,
    };

    private static WGPUExtent3D Extent(TextureCopyRegion region) => new() { width = region.Width, height = region.Height, depthOrArrayLayers = region.ArrayLayerCount };

    private void Initialize()
    {
        _encoder = WGPU.wgpuDeviceCreateCommandEncoder(_owner.NativeDevice.Handle, null);
        if (_encoder == null)
        {
            throw new InvalidOperationException("WebGPU command encoder creation failed.");
        }
    }

    private void CopyBufferNative<TSource, TDestination>(WgpuBuffer<TSource> source, ulong sourceOffset, WgpuBuffer<TDestination> destination, ulong destinationOffset, ulong size)
        where TSource : unmanaged
        where TDestination : unmanaged
        => WGPU.wgpuCommandEncoderCopyBufferToBuffer(_encoder, source.Native.Handle, sourceOffset, destination.Native.Handle, destinationOffset, size);

    private void CopyTextureNative(TextureCopyRegion source, TextureCopyRegion destination)
    {
        WGPUTexelCopyTextureInfo src = TextureInfo(source);
        WGPUTexelCopyTextureInfo dst = TextureInfo(destination);
        WGPUExtent3D size = Extent(source);
        WGPU.wgpuCommandEncoderCopyTextureToTexture(_encoder, &src, &dst, &size);
    }

    private void CopyBufferTextureNative(BufferTextureCopyLayout buffer, TextureCopyRegion region, bool upload)
    {
        var bytes = new WGPUTexelCopyBufferInfo
        {
            buffer = ((WgpuBuffer<byte>)buffer.Buffer.Buffer).Native.Handle,
            layout = new() { offset = buffer.Buffer.OffsetInBytes, bytesPerRow = buffer.BytesPerRow, rowsPerImage = buffer.RowsPerImage },
        };
        WGPUTexelCopyTextureInfo texture = TextureInfo(region);
        WGPUExtent3D size = Extent(region);
        if (upload)
        {
            WGPU.wgpuCommandEncoderCopyBufferToTexture(_encoder, &bytes, &texture, &size);
        }
        else
        {
            WGPU.wgpuCommandEncoderCopyTextureToBuffer(_encoder, &texture, &bytes, &size);
        }
    }

    private void MemoryBarrierNative(MemoryBarrierDesc barrier)
    {
        // WebGPU provides visibility and transitions for ordered resource usages.
    }

    private void BufferBarrierNative<T>(WgpuBuffer<T> buffer, BufferBarrierDesc<T> barrier)
        where T : unmanaged
    {
        // The explicit dependency is validated by the command layer.
    }

    private void TextureBarrierNative(WgpuTexture texture, TextureBarrierDesc barrier)
    {
        // Logical states are validated; WebGPU owns physical image transitions.
    }

    private IRenderEncoder BeginRenderNative(RenderColorAttachmentDesc[] attachments)
    {
        Span<WGPURenderPassColorAttachment> colors = stackalloc WGPURenderPassColorAttachment[attachments.Length];
        for (int i = 0; i < attachments.Length; i++)
        {
            RenderColorAttachmentDesc a = attachments[i];
            colors[i] = new()
            {
                view = ((WgpuTextureView)a.View).Native.Handle,
                depthSlice = uint.MaxValue,
                loadOp = a.LoadOp == AttachmentLoadOp.Clear ? WGPULoadOp.Clear : WGPULoadOp.Load,
                storeOp = a.StoreOp == AttachmentStoreOp.Store ? WGPUStoreOp.Store : WGPUStoreOp.Discard,
                clearValue = new() { r = a.ClearValue.Red, g = a.ClearValue.Green, b = a.ClearValue.Blue, a = a.ClearValue.Alpha },
            };
        }

        fixed (WGPURenderPassColorAttachment* data = colors)
        {
            var desc = new WGPURenderPassDescriptor { colorAttachmentCount = (nuint)colors.Length, colorAttachments = data };
            WGPURenderPassEncoderImpl* pass = WGPU.wgpuCommandEncoderBeginRenderPass(_encoder, &desc);
            if (pass == null)
            {
                throw new InvalidOperationException("WebGPU render pass creation failed.");
            }

            return new WgpuRenderEncoder(this, pass);
        }
    }

    private IComputeEncoder BeginComputeNative()
    {
        WGPUComputePassEncoderImpl* pass = WGPU.wgpuCommandEncoderBeginComputePass(_encoder, null);
        if (pass == null)
        {
            throw new InvalidOperationException("WebGPU compute pass creation failed.");
        }

        return new WgpuComputeEncoder(this, pass);
    }

    private void FinishNative()
    {
        _command = WGPU.wgpuCommandEncoderFinish(_encoder, null);
        if (_command == null)
        {
            State = CommandBufferState.Faulted;
            throw new InvalidOperationException("WebGPU command finalization failed.");
        }

        WGPU.wgpuCommandEncoderRelease(_encoder);
        _encoder = null;
    }

    private void DisposeNative()
    {
        if (_encoder != null)
        {
            WGPU.wgpuCommandEncoderRelease(_encoder);
        }

        if (_command != null)
        {
            WGPU.wgpuCommandBufferRelease(_command);
        }
    }
}
