using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuCommands(CpuGraphicsDevice owner) : CpuResource(owner), IGraphicsCommandBuffer
{
    private bool _pass;

    public CommandBufferState State
    {
        get;
        private set;
    }

     = CommandBufferState.Recording;

    internal uint PassWidth
    {
        get;
        private set;
    }

    internal uint PassHeight
    {
        get;
        private set;
    }

    internal bool IsCompute
    {
        get;
        private set;
    }

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)

        where TSource : unmanaged

        where TDestination : unmanaged
    {
        Recording();
        Buffer(source, BufferUsage.CopySource);
        Buffer(destination, BufferUsage.CopyDestination);
        if (source.SizeInBytes != destination.SizeInBytes)
        {
            throw new ArgumentException("Copy sizes differ.");
        }

        Owner.CopyCount++;
        Owner.Mix(source.SizeInBytes);
    }

    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)

        where T : struct, IShaderArguments
    {
        Recording();
        Shader(source);
        Shader(destination);
        if (source.Count != destination.Count || source.Buffer.Memory != MemoryPreference.Upload || destination.Buffer.Memory != MemoryPreference.Automatic)
        {
            throw new ArgumentException("Shader copy requires matching upload and GPU ranges.");
        }

        Owner.CopyCount++;
        Owner.Mix(source.Count * source.Buffer.ShaderElementStrideInBytes);
    }

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination)
    {
        Recording();
        Region(source, TextureUsage.CopySource);
        Region(destination, TextureUsage.CopyDestination);
        if (source.Width != destination.Width || source.Height != destination.Height || source.ArrayLayerCount != destination.ArrayLayerCount || source.Texture.Format != destination.Texture.Format)
        {
            throw new ArgumentException("Copy regions differ.");
        }

        Owner.CopyCount++;
    }

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination)
    {
        Recording();
        Region(destination, TextureUsage.CopyDestination);
        Rows(source, destination, BufferUsage.CopySource);
        Owner.CopyCount++;
    }

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => throw new NotSupportedException("GPU readback is not modeled.");

    public void Barrier(MemoryBarrierDesc barrier)
    {
        Recording();
        Owner.Barriers++;
    }

    public void Barrier<T>(BufferBarrierDesc<T> barrier)

        where T : unmanaged
    {
        Recording();
        Buffer(barrier.Buffer);
        Owner.Barriers++;
    }

    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)

        where T : struct, IShaderArguments
    {
        Recording();
        Shader(barrier.Buffer);
        Owner.Barriers++;
    }

    public void Barrier(TextureBarrierDesc barrier)
    {
        Recording();
        Owner.Own(barrier.Texture);
        TextureSubresourceRange r = barrier.Range;
        if (r.MipLevelCount == 0 || r.BaseMipLevel >= barrier.Texture.MipLevels || r.MipLevelCount > barrier.Texture.MipLevels - r.BaseMipLevel || r.ArrayLayerCount == 0 || r.BaseArrayLayer >= barrier.Texture.ArrayLayers || r.ArrayLayerCount > barrier.Texture.ArrayLayers - r.BaseArrayLayer)
        {
            throw new ArgumentOutOfRangeException(nameof(barrier));
        }

        Owner.Barriers++;
    }

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc)
    {
        Recording();
        if (desc.ColorAttachments.Count == 0 && desc.DepthStencilAttachment == null)
        {
            throw new ArgumentException("Render _pass requires attachments.");
        }

        PassWidth = PassHeight = 0;
        IsCompute = false;
        foreach (RenderColorAttachmentDesc a in desc.ColorAttachments)
        {
            Attachment(a.View);
        }

        if (desc.DepthStencilAttachment != null)
        {
            Attachment(desc.DepthStencilAttachment.View);
        }

        _pass = true;
        Owner.Renders++;
        return new CpuEncoder(this);
    }

    public IComputeEncoder BeginComputePass(ComputePassDesc desc)
    {
        Recording();
        IsCompute = true;
        _pass = true;
        Owner.Computes++;
        return new CpuEncoder(this);
    }

    public void Finish()
    {
        Recording();
        State = CommandBufferState.Executable;
    }

    public override void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        if (_pass)
        {
            throw new InvalidOperationException("End the _pass before disposing commands.");
        }

        State = CommandBufferState.Disposed;
        base.Dispose();
    }

    internal void Recording(bool inside = false)
    {
        Check();
        if (State != CommandBufferState.Recording || _pass != inside)
        {
            throw new InvalidOperationException("Invalid recording/_pass state.");
        }
    }

    internal void EndPass()
    {
        Recording(true);
        _pass = false;
    }

    internal void Submit()
    {
        Check();
        if (State != CommandBufferState.Executable)
        {
            throw new InvalidOperationException("Command buffer must be executable and one-shot.");
        }

        State = CommandBufferState.Submitted;
        Owner.Mix(Id);
        State = CommandBufferState.Completed;
    }

    private void Attachment(IGraphicsTextureView view)
    {
        Owner.Own(view);
        if (view.Info.MipLevelCount != 1 || view.Info.ArrayLayerCount != 1)
        {
            throw new ArgumentException("Attachment must select one mip/layer.");
        }

        (uint w, uint h) = view.Texture.GetMipSize(view.Info.BaseMipLevel);
        if (PassWidth != 0 && (PassWidth != w || PassHeight != h))
        {
            throw new ArgumentException("Attachment dimensions differ.");
        }

        PassWidth = w;
        PassHeight = h;
    }

    private void Buffer<T>(BufferSlice<T> range, BufferUsage usage = 0)

        where T : unmanaged
    {
        Owner.Own(range.Buffer);
        range.Buffer.ValidateByteRange(range.OffsetInBytes, range.SizeInBytes);
        if (range.Buffer.IsMapped || (range.Buffer.Usage & usage) != usage)
        {
            throw new InvalidOperationException("Mapped buffer or invalid usage.");
        }
    }

    private void Shader<T>(ShaderDataSlice<T> range)

        where T : struct, IShaderArguments
    {
        Owner.Own(range.Buffer);
        if (range.Count == 0 || range.Buffer.IsMapped)
        {
            throw new InvalidOperationException("Invalid shader range.");
        }
    }

    private void Region(TextureCopyRegion r, TextureUsage usage)
    {
        Owner.Own(r.Texture);
        (uint w, uint h) = r.Texture.GetMipSize(r.MipLevel);
        if (r.Width == 0 || r.Height == 0 || r.OriginX > w || r.Width > w - r.OriginX || r.OriginY > h || r.Height > h - r.OriginY || r.ArrayLayerCount == 0 || r.BaseArrayLayer >= r.Texture.ArrayLayers || r.ArrayLayerCount > r.Texture.ArrayLayers - r.BaseArrayLayer || (r.Texture.Usage & usage) != usage)
        {
            throw new ArgumentOutOfRangeException(nameof(r));
        }
    }

    private void Rows(BufferTextureCopyLayout b, TextureCopyRegion r, BufferUsage usage)
    {
        Buffer(b.Buffer, usage);
        ulong row = (ulong)r.Width * Owner.GetTextureCopyLayout(r.Texture.Format).BytesPerTexel;
        if (b.BytesPerRow < row || b.RowsPerImage < r.Height)
        {
            throw new ArgumentException("Invalid row layout.");
        }

        ulong required = checked(((ulong)b.BytesPerRow * (((ulong)b.RowsPerImage * (r.ArrayLayerCount - 1)) + r.Height - 1)) + row);
        if (required > b.Buffer.SizeInBytes)
        {
            throw new ArgumentException("Buffer copy range too small.");
        }
    }
}
