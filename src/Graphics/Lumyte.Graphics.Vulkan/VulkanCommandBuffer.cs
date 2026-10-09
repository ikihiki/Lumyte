using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe partial class VulkanCommandBuffer : IGraphicsCommandBuffer
{
    private readonly VulkanDevice _owner;
    private readonly List<Action> _resources = [];
    private readonly Dictionary<(IGraphicsTexture Texture, uint Mip, uint Layer), TextureState> _states = [];
    private object? _active;

    internal VulkanCommandBuffer(VulkanDevice owner)
    {
        _owner = owner;
        Initialize();
    }

    public CommandBufferState State { get; private set; } = CommandBufferState.Recording;

    internal VulkanDevice Owner => _owner;

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        RequireRecording();
        VulkanBuffer<TSource> src = Buffer(source, BufferUsage.CopySource);
        VulkanBuffer<TDestination> dst = Buffer(destination, BufferUsage.CopyDestination);
        if (ReferenceEquals(src, dst) || source.SizeInBytes != destination.SizeInBytes || source.OffsetInBytes % src.Layout.CopyOffsetAlignmentInBytes != 0 ||
            destination.OffsetInBytes % dst.Layout.CopyOffsetAlignmentInBytes != 0 || source.SizeInBytes % src.Layout.CopySizeAlignmentInBytes != 0 || destination.SizeInBytes % dst.Layout.CopySizeAlignmentInBytes != 0)
        {
            throw new ArgumentException("Invalid GPU copy range, allocation or alignment.");
        }

        CopyBufferNative(src, source.OffsetInBytes, dst, destination.OffsetInBytes, source.SizeInBytes);
        _resources.Add(() => { _ = src.Native; });
        _resources.Add(() => { _ = dst.Native; });
    }

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination)
    {
        RequireRecording();
        VulkanTexture src = Texture(source, TextureUsage.CopySource);
        VulkanTexture dst = Texture(destination, TextureUsage.CopyDestination);
        if (ReferenceEquals(src, dst) || source.Texture.Format != destination.Texture.Format || source.Width != destination.Width || source.Height != destination.Height || source.ArrayLayerCount != destination.ArrayLayerCount)
        {
            throw new ArgumentException("Texture copies require distinct allocations and equal format and extent.");
        }

        RequireState(source, TextureState.CopySource);
        RequireState(destination, TextureState.CopyDestination);
        CopyTextureNative(source, destination);
        Keep(src);
        Keep(dst);
    }

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination)
    {
        RequireRecording();
        VulkanTexture texture = Texture(destination, TextureUsage.CopyDestination);
        ArgumentNullException.ThrowIfNull(source);
        VulkanBuffer<byte> buffer = Buffer(source.Buffer, BufferUsage.CopySource);
        CommandValidation.Layout(source, destination, _owner.GetTextureCopyLayout(texture.Format));
        RequireState(destination, TextureState.CopyDestination);
        CopyBufferTextureNative(source, destination, true);
        _resources.Add(() => { _ = buffer.Native; });
        Keep(texture);
    }

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination)
    {
        RequireRecording();
        VulkanTexture texture = Texture(source, TextureUsage.CopySource);
        ArgumentNullException.ThrowIfNull(destination);
        VulkanBuffer<byte> buffer = Buffer(destination.Buffer, BufferUsage.CopyDestination);
        CommandValidation.Layout(destination, source, _owner.GetTextureCopyLayout(texture.Format));
        RequireState(source, TextureState.CopySource);
        CopyBufferTextureNative(destination, source, false);
        _resources.Add(() => { _ = buffer.Native; });
        Keep(texture);
    }

    public void Barrier(MemoryBarrierDesc barrier)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(barrier);
        CommandValidation.Scope(barrier.Before);
        CommandValidation.Scope(barrier.After);
        MemoryBarrierNative(barrier);
    }

    public void Barrier<T>(BufferBarrierDesc<T> barrier)
        where T : unmanaged
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(barrier);
        VulkanBuffer<T> buffer = Buffer(barrier.Buffer, 0);
        CommandValidation.Scope(barrier.Before);
        CommandValidation.Scope(barrier.After);
        CommandValidation.BufferAccess(buffer, barrier.Before);
        CommandValidation.BufferAccess(buffer, barrier.After);
        BufferBarrierNative(buffer, barrier);
        _resources.Add(() => { _ = buffer.Native; });
    }

    public void Barrier(TextureBarrierDesc barrier)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(barrier);
        VulkanTexture texture = OwnTexture(barrier.Texture);
        CommandValidation.Range(texture, barrier.Range);
        CommandValidation.TextureState(texture, barrier.BeforeState, false);
        CommandValidation.TextureState(texture, barrier.AfterState, true);
        CommandValidation.Scope(barrier.Before);
        CommandValidation.Scope(barrier.After);
        CommandValidation.TextureAccess(barrier.BeforeState, barrier.Before);
        CommandValidation.TextureAccess(barrier.AfterState, barrier.After);
        for (uint mip = 0; mip < barrier.Range.MipLevelCount; mip++)
        {
            for (uint layer = 0; layer < barrier.Range.ArrayLayerCount; layer++)
            {
                (IGraphicsTexture, uint, uint) key = ((IGraphicsTexture)texture, barrier.Range.BaseMipLevel + mip, barrier.Range.BaseArrayLayer + layer);
                if (barrier.BeforeState != TextureState.Undefined && _states.TryGetValue(key, out TextureState previous) && previous != barrier.BeforeState)
                {
                    throw new ArgumentException("Texture BeforeState conflicts with a recorded transition.");
                }
            }
        }

        TextureBarrierNative(texture, barrier);
        for (uint mip = 0; mip < barrier.Range.MipLevelCount; mip++)
        {
            for (uint layer = 0; layer < barrier.Range.ArrayLayerCount; layer++)
            {
                _states[(texture, barrier.Range.BaseMipLevel + mip, barrier.Range.BaseArrayLayer + layer)] = barrier.AfterState;
            }
        }

        Keep(texture);
    }

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.ColorAttachments);
        RenderColorAttachmentDesc[] attachments = desc.ColorAttachments.ToArray();
        if (attachments.Length == 0 || (uint)attachments.Length > _owner.Caps.MaxColorAttachments)
        {
            throw new ArgumentException("Invalid color attachment count.");
        }

        var seen = new HashSet<(IGraphicsTexture, uint, uint)>();
        (uint Width, uint Height)? size = null;
        foreach (RenderColorAttachmentDesc attachment in attachments)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            if (attachment.View is not VulkanTextureView view || !ReferenceEquals(view.Owner, _owner))
            {
                throw new ArgumentException("Attachment belongs to another device.");
            }

            _ = view.Native;
            TextureViewInfo info = view.Info;
            VulkanTexture texture = OwnTexture(view.Texture);
            (uint width, uint height) = texture.GetMipSize(info.BaseMipLevel);
            ClearColor clear = attachment.ClearValue;
            if ((texture.Usage & TextureUsage.RenderAttachment) == 0 || info.Dimension != TextureViewDimension.D2 || info.MipLevelCount != 1 || info.ArrayLayerCount != 1 ||
                !seen.Add((texture, info.BaseMipLevel, info.BaseArrayLayer)) || (size is { } expected && expected != (width, height)) ||
                !Enum.IsDefined(attachment.LoadOp) || !Enum.IsDefined(attachment.StoreOp) || !double.IsFinite(clear.Red) || !double.IsFinite(clear.Green) || !double.IsFinite(clear.Blue) || !double.IsFinite(clear.Alpha))
            {
                throw new ArgumentException("Invalid color attachment, operation or dimensions.");
            }

            RequireState(new() { Texture = texture, MipLevel = info.BaseMipLevel, BaseArrayLayer = info.BaseArrayLayer, Width = width, Height = height }, TextureState.ColorAttachment);
            size = (width, height);
        }

        IRenderEncoder pass = BeginRenderNative(attachments);
        foreach (RenderColorAttachmentDesc attachment in attachments)
        {
            var view = (VulkanTextureView)attachment.View;
            _resources.Add(() => { _ = view.Native; });
        }

        _active = pass;
        return pass;
    }

    public IComputeEncoder BeginComputePass(ComputePassDesc desc)
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(desc);
        IComputeEncoder pass = BeginComputeNative();
        _active = pass;
        return pass;
    }

    public void Finish()
    {
        RequireRecording();
        FinishNative();
        State = CommandBufferState.Executable;
    }

    public void Dispose()
    {
        if (State == CommandBufferState.Disposed)
        {
            return;
        }

        if (_active != null || State == CommandBufferState.Submitted)
        {
            throw new InvalidOperationException("End the pass and complete submission before disposal.");
        }

        DisposeNative();
        _resources.Clear();
        _states.Clear();
        State = CommandBufferState.Disposed;
        _owner.ReleaseCommand();
    }

    internal void ValidateSubmit()
    {
        _owner.ValidateAlive();
        if (State != CommandBufferState.Executable)
        {
            throw new InvalidOperationException("Only executable, never-submitted commands may be submitted.");
        }

        foreach (Action validate in _resources)
        {
            validate();
        }
    }

    internal void MarkSubmitted() => State = CommandBufferState.Submitted;

    internal void Complete(bool success) => State = success ? CommandBufferState.Completed : CommandBufferState.Faulted;

    internal void TrackProgram(Action validate) => _resources.Add(validate);

    internal void ValidatePass(object pass) => ValidateEnd(pass);

    private void RequireRecording()
    {
        _owner.ValidateAlive();
        ObjectDisposedException.ThrowIf(State == CommandBufferState.Disposed, this);
        if (State != CommandBufferState.Recording || _active != null)
        {
            throw new InvalidOperationException("Commands require Recording state outside a pass.");
        }
    }

    private VulkanBuffer<T> Buffer<T>(BufferSlice<T> slice, BufferUsage usage)
        where T : unmanaged
    {
        if (slice.Buffer is not VulkanBuffer<T> buffer || !ReferenceEquals(buffer.Owner, _owner) || (buffer.Usage & usage) != usage)
        {
            throw new ArgumentException("Buffer belongs to another device or lacks required usage.");
        }

        buffer.ValidateRange(slice.OffsetInBytes, slice.SizeInBytes);
        _ = buffer.Native;
        return buffer;
    }

    private VulkanTexture OwnTexture(IGraphicsTexture resource)
    {
        if (resource is not VulkanTexture texture || !ReferenceEquals(texture.Owner, _owner))
        {
            throw new ArgumentException("Texture belongs to another device.");
        }

        _ = texture.Native;
        return texture;
    }

    private VulkanTexture Texture(TextureCopyRegion region, TextureUsage usage)
    {
        CommandValidation.Region(region, usage);
        return OwnTexture(region.Texture);
    }

    private void RequireState(TextureCopyRegion region, TextureState state)
    {
        for (uint layer = 0; layer < region.ArrayLayerCount; layer++)
        {
            if (!_states.TryGetValue((region.Texture, region.MipLevel, region.BaseArrayLayer + layer), out TextureState current) || current != state)
            {
                throw new InvalidOperationException("Declare the texture state with an explicit barrier before use.");
            }
        }
    }

    private void Keep(VulkanTexture texture) => _resources.Add(() => { _ = texture.Native; });

    private void ValidateEnd(object pass)
    {
        if (State != CommandBufferState.Recording || !ReferenceEquals(_active, pass))
        {
            throw new InvalidOperationException("This pass is not active.");
        }
    }
}
