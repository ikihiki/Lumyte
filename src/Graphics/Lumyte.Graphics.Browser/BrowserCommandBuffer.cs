using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed partial class BrowserCommandBuffer : IGraphicsCommandBuffer
{
    private readonly ShaderDataTransferState _shaderData = new();
    private readonly List<IDisposable> _bindings = [];
    private readonly BrowserDevice _owner;
    private readonly List<Action> _resources = [];
    private readonly Dictionary<(IGraphicsTexture Texture, uint Mip, uint Layer), TextureState> _states = [];
    private readonly HashSet<SurfaceFrameLifetime> _surfaceFrames = [];
    private object? _active;

    internal BrowserCommandBuffer(BrowserDevice owner)
    {
        _owner = owner;
        Initialize();
    }

    public CommandBufferState State { get; private set; } = CommandBufferState.Recording;

    internal BrowserDevice Owner => _owner;

    internal ShaderDataTransferState ShaderDataTransfers => _shaderData;

    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
        where T : struct, IShaderData
    {
        RequireRecording();
        if (source.Buffer is not BrowserShaderDataBuffer<T> src || destination.Buffer is not BrowserShaderDataBuffer<T> dst || !ReferenceEquals(src.Owner, _owner) || !ReferenceEquals(dst.Owner, _owner) || source.Count != destination.Count)
        {
            throw new ArgumentException("Shader data copy requires equal ranges from this device.");
        }

        _shaderData.Record(src, source.Offset, dst, destination.Offset, source.Count, () => CopyBuffer(src.Storage.Slice(checked(source.Offset * src.ShaderElementStrideInBytes), checked(source.Count * src.ShaderElementStrideInBytes)), dst.Storage.Slice(checked(destination.Offset * dst.ShaderElementStrideInBytes), checked(destination.Count * dst.ShaderElementStrideInBytes))), TrackProgram);
    }

    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
        where T : struct, IShaderData
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(barrier);
        if (barrier.Buffer.Buffer is not BrowserShaderDataBuffer<T> buffer || !ReferenceEquals(buffer.Owner, _owner))
        {
            throw new ArgumentException("Shader data barrier belongs to another device.");
        }

        Barrier(new BufferBarrierDesc<byte> { Buffer = buffer.Storage.Slice(checked(barrier.Buffer.Offset * buffer.ShaderElementStrideInBytes), checked(barrier.Buffer.Count * buffer.ShaderElementStrideInBytes)), Before = barrier.Before, After = barrier.After });
    }

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        RequireRecording();
        BrowserBuffer<TSource> src = Buffer(source, BufferUsage.CopySource);
        BrowserBuffer<TDestination> dst = Buffer(destination, BufferUsage.CopyDestination);
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
        BrowserTexture src = Texture(source, TextureUsage.CopySource);
        BrowserTexture dst = Texture(destination, TextureUsage.CopyDestination);
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
        BrowserTexture texture = Texture(destination, TextureUsage.CopyDestination);
        ArgumentNullException.ThrowIfNull(source);
        BrowserBuffer<byte> buffer = Buffer(source.Buffer, BufferUsage.CopySource);
        CommandValidation.Layout(source, destination, _owner.GetTextureCopyLayout(texture.Format));
        RequireState(destination, TextureState.CopyDestination);
        CopyBufferTextureNative(source, destination, true);
        _resources.Add(() => { _ = buffer.Native; });
        Keep(texture);
    }

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination)
    {
        RequireRecording();
        BrowserTexture texture = Texture(source, TextureUsage.CopySource);
        ArgumentNullException.ThrowIfNull(destination);
        BrowserBuffer<byte> buffer = Buffer(destination.Buffer, BufferUsage.CopyDestination);
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
        BrowserBuffer<T> buffer = Buffer(barrier.Buffer, 0);
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
        BrowserTexture texture = OwnTexture(barrier.Texture);
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
        if ((attachments.Length == 0 && desc.DepthStencilAttachment == null) || (uint)attachments.Length > _owner.Caps.MaxColorAttachments)
        {
            throw new ArgumentException("Invalid color attachment count.");
        }

        var seen = new HashSet<(IGraphicsTexture, uint, uint)>();
        (uint Width, uint Height)? size = null;
        foreach (RenderColorAttachmentDesc attachment in attachments)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            if (attachment.View is not BrowserTextureView view || !ReferenceEquals(view.Owner, _owner))
            {
                throw new ArgumentException("Attachment belongs to another device.");
            }

            _ = view.Native;
            TextureViewInfo info = view.Info;
            BrowserTexture texture = OwnTexture(view.Texture);
            (uint width, uint height) = texture.GetMipSize(info.BaseMipLevel);
            ClearColor clear = attachment.ClearValue;
            if (info.Format is TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8 || (texture.Usage & TextureUsage.RenderAttachment) == 0 || info.Dimension != TextureViewDimension.D2 || info.MipLevelCount != 1 || info.ArrayLayerCount != 1 ||
                !seen.Add((texture, info.BaseMipLevel, info.BaseArrayLayer)) || (size is { } expected && expected != (width, height)) ||
                !Enum.IsDefined(attachment.LoadOp) || !Enum.IsDefined(attachment.StoreOp) || !double.IsFinite(clear.Red) || !double.IsFinite(clear.Green) || !double.IsFinite(clear.Blue) || !double.IsFinite(clear.Alpha))
            {
                throw new ArgumentException("Invalid color attachment, operation or dimensions.");
            }

            RequireState(new() { Texture = texture, MipLevel = info.BaseMipLevel, BaseArrayLayer = info.BaseArrayLayer, Width = width, Height = height }, TextureState.ColorAttachment);
            size = (width, height);
        }

        RenderDepthStencilAttachmentDesc? depth = desc.DepthStencilAttachment;
        if (depth != null)
        {
            if (depth.View is not BrowserTextureView view || !ReferenceEquals(view.Owner, _owner))
            {
                throw new ArgumentException("Depth attachment belongs to another device.");
            }

            _ = view.Native;
            TextureViewInfo info = view.Info;
            BrowserTexture texture = OwnTexture(view.Texture);
            (uint width, uint height) = texture.GetMipSize(info.BaseMipLevel);
            if (info.Format is not (TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8) ||
                (texture.Usage & TextureUsage.RenderAttachment) == 0 || info.Dimension != TextureViewDimension.D2 || info.MipLevelCount != 1 || info.ArrayLayerCount != 1 ||
                (size is { } expected && expected != (width, height)) || !Enum.IsDefined(depth.DepthLoadOp) || !Enum.IsDefined(depth.DepthStoreOp) ||
                !Enum.IsDefined(depth.StencilLoadOp) || !Enum.IsDefined(depth.StencilStoreOp) || !float.IsFinite(depth.DepthClearValue) ||
                depth.DepthClearValue < 0 || depth.DepthClearValue > 1 || depth.StencilClearValue > 255)
            {
                throw new ArgumentException("Invalid depth/stencil attachment, operation or dimensions.");
            }

            RequireState(new() { Texture = texture, MipLevel = info.BaseMipLevel, BaseArrayLayer = info.BaseArrayLayer, Width = width, Height = height }, TextureState.DepthStencilAttachment);
        }

        IRenderEncoder pass = BeginRenderNative(attachments, depth);
        if (depth != null)
        {
            var view = (BrowserTextureView)depth.View;
            _resources.Add(() => { _ = view.Native; });
        }

        foreach (RenderColorAttachmentDesc attachment in attachments)
        {
            var view = (BrowserTextureView)attachment.View;
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
        foreach (IDisposable binding in _bindings)
        {
            binding.Dispose();
        }

        _bindings.Clear();
        _resources.Clear();
        _states.Clear();
        State = CommandBufferState.Disposed;
        _owner.ReleaseCommand();
    }

    internal ShaderValueSnapshot ReadShaderData(IShaderDataSource source, ulong index) => _shaderData.Read(source, index, TrackProgram);

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

    internal TextureState? ValidateSurfaceSubmission(SurfaceFrameLifetime? frame)
    {
        if (_surfaceFrames.Any(used => !ReferenceEquals(used, frame)))
        {
            throw new InvalidOperationException("Submit acquired images with their explicit frame and no other frame.");
        }

        return _states.Where(pair => pair.Key.Texture is BrowserTexture texture && ReferenceEquals(texture.SurfaceFrame, frame) && frame != null).Select(pair => (TextureState?)pair.Value).LastOrDefault();
    }

    internal void MarkSubmitted()
    {
        _shaderData.Publish();
        State = CommandBufferState.Submitted;
    }

    internal void Complete(bool success) => State = success ? CommandBufferState.Completed : CommandBufferState.Faulted;

    internal void KeepBinding(IDisposable binding) => _bindings.Add(binding);

    internal void TrackShaderSnapshot(ShaderBindingSnapshot snapshot)
    {
        foreach (BrowserTextureView view in snapshot.References.Select(reference => reference.Resource).OfType<BrowserTextureView>())
        {
            Keep(OwnTexture(view.Texture));
            _resources.Add(() => { _ = view.Native; });
        }

        _resources.Add(snapshot.Validate);
    }

    internal void TrackProgram(Action validate) => _resources.Add(validate);

    internal void ValidatePass(object pass) => ValidateEnd(pass);

    internal BrowserBuffer<T> Buffer<T>(BufferSlice<T> slice, BufferUsage usage)
        where T : unmanaged
    {
        if (slice.Buffer is not BrowserBuffer<T> buffer || !ReferenceEquals(buffer.Owner, _owner) || (buffer.Usage & usage) != usage)
        {
            throw new ArgumentException("Buffer belongs to another device or lacks required usage.");
        }

        buffer.ValidateRange(slice.OffsetInBytes, slice.SizeInBytes);
        _ = buffer.Native;
        return buffer;
    }

    private void RequireRecording()
    {
        _owner.ValidateAlive();
        ObjectDisposedException.ThrowIf(State == CommandBufferState.Disposed, this);
        if (State != CommandBufferState.Recording || _active != null)
        {
            throw new InvalidOperationException("Commands require Recording state outside a pass.");
        }
    }

    private BrowserTexture OwnTexture(IGraphicsTexture resource)
    {
        if (resource is not BrowserTexture texture || !ReferenceEquals(texture.Owner, _owner))
        {
            throw new ArgumentException("Texture belongs to another device.");
        }

        _ = texture.Native;
        return texture;
    }

    private BrowserTexture Texture(TextureCopyRegion region, TextureUsage usage)
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

    private void Keep(BrowserTexture texture)
    {
        _resources.Add(() => { _ = texture.Native; });
        if (texture.SurfaceFrame is { } frame)
        {
            _surfaceFrames.Add(frame);
        }
    }

    private void ValidateEnd(object pass)
    {
        if (State != CommandBufferState.Recording || !ReferenceEquals(_active, pass))
        {
            throw new InvalidOperationException("This pass is not active.");
        }
    }
}
