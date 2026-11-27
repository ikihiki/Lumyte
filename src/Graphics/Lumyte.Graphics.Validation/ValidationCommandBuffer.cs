using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed partial class ValidationCommandBuffer : ValidationResource<IGraphicsCommandBuffer>, IGraphicsCommandBuffer
{
    private readonly Dictionary<IShaderDataSource, IShaderReferenceMetadata> _overlay = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IShaderDataSource, Action<IShaderReferenceMetadata>> _publish = new(ReferenceEqualityComparer.Instance);
    private object? _pass;
    private bool _submitted;
    private bool _recordingStarted;

    internal ValidationCommandBuffer(ValidationDevice device, IGraphicsCommandBuffer raw, CommandBufferDesc desc)
        : base(device, raw, device)
    {
    }

    public CommandBufferState State => IsDisposed ? CommandBufferState.Disposed : Raw.State;

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        OutsidePass();
        BufferSlice<TSource> src = Unwrap(source);
        BufferSlice<TDestination> dst = Unwrap(destination);
        using GraphicsValidation.RecordingTransaction tx = Device.Tracker.BeginRecording(this);
        Device.Tracker.Access(this, source.Buffer, new(PipelineStage.Copy, ResourceAccess.CopyRead), source.OffsetInBytes, source.SizeInBytes);
        Device.Tracker.Access(this, destination.Buffer, new(PipelineStage.Copy, ResourceAccess.CopyWrite), destination.OffsetInBytes, destination.SizeInBytes);
        Raw.CopyBuffer(src, dst);
        tx.Commit();
    }

    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
        where T : struct, IShaderArguments
    {
        OutsidePass();
        ShaderDataSlice<ValidationArguments<T>> src = Unwrap(source);
        ShaderDataSlice<ValidationArguments<T>> dst = Unwrap(destination);
        using GraphicsValidation.RecordingTransaction tx = Device.Tracker.BeginRecording(this);
        var sourceData = (IShaderDataSource)source.Buffer;
        var destinationData = (IShaderDataSource)destination.Buffer;
        IShaderReferenceMetadata metadata = ReadShaderDependencies(sourceData);
        if (destination.Offset != 0 || destination.Count != destination.Buffer.Count)
        {
            IShaderReferenceMetadata previous = ReadShaderDependencies(destinationData);
            metadata = new ShaderDependencyMetadata(previous.References.Concat(metadata.References));
        }

        Device.Tracker.Access(this, source.Buffer, new(PipelineStage.Copy, ResourceAccess.CopyRead), checked(source.Offset * source.Buffer.ShaderElementStrideInBytes), checked(source.Count * source.Buffer.ShaderElementStrideInBytes));
        Device.Tracker.Access(this, destination.Buffer, new(PipelineStage.Copy, ResourceAccess.CopyWrite), checked(destination.Offset * destination.Buffer.ShaderElementStrideInBytes), checked(destination.Count * destination.Buffer.ShaderElementStrideInBytes));
        Device.Tracker.Metadata(this, destinationData, metadata, true);
        Raw.CopyBuffer(src, dst);
        _overlay[destinationData] = metadata;
        _publish[destinationData] = ((ValidationShaderDataBuffer<T>)destination.Buffer).PublishDependencies;
        tx.Commit();
    }

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination)
    {
        OutsidePass();
        TextureCopyRegion src = Unwrap(source);
        TextureCopyRegion dst = Unwrap(destination);
        using GraphicsValidation.RecordingTransaction tx = Device.Tracker.BeginRecording(this);
        TextureCopyAccess(source, false);
        TextureCopyAccess(destination, true);
        Raw.CopyTexture(src, dst);
        tx.Commit();
    }

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination)
    {
        OutsidePass();
        BufferTextureCopyLayout src = Unwrap(source);
        TextureCopyRegion dst = Unwrap(destination);
        ulong footprint = BufferTextureFootprint(source, destination);
        using GraphicsValidation.RecordingTransaction tx = Device.Tracker.BeginRecording(this);
        Device.Tracker.Access(this, source.Buffer.Buffer, new(PipelineStage.Copy, ResourceAccess.CopyRead), source.Buffer.OffsetInBytes, footprint);
        TextureCopyAccess(destination, true);
        Raw.CopyBufferToTexture(src, dst);
        tx.Commit();
    }

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination)
    {
        OutsidePass();
        TextureCopyRegion src = Unwrap(source);
        BufferTextureCopyLayout dst = Unwrap(destination);
        ulong footprint = BufferTextureFootprint(destination, source);
        using GraphicsValidation.RecordingTransaction tx = Device.Tracker.BeginRecording(this);
        TextureCopyAccess(source, false);
        Device.Tracker.Access(this, destination.Buffer.Buffer, new(PipelineStage.Copy, ResourceAccess.CopyWrite), destination.Buffer.OffsetInBytes, footprint);
        Raw.CopyTextureToBuffer(src, dst);
        tx.Commit();
    }

    public void Barrier(MemoryBarrierDesc barrier)
    {
        OutsidePass();
        barrier = barrier with { Before = ValidationStages.Normalize(Device, barrier.Before), After = ValidationStages.Normalize(Device, barrier.After) };
        using GraphicsValidation.RecordingTransaction tx = Device.Tracker.BeginRecording(this);
        Device.Tracker.MemoryBarrier(this, barrier.Before, barrier.After);
        Raw.Barrier(barrier);
        tx.Commit();
    }

    public void Barrier<T>(BufferBarrierDesc<T> barrier)
        where T : unmanaged
    {
        OutsidePass();
        barrier = barrier with { Before = ValidationStages.Normalize(Device, barrier.Before), After = ValidationStages.Normalize(Device, barrier.After) };
        BufferBarrierDesc<T> native = barrier with { Buffer = Unwrap(barrier.Buffer) };
        using GraphicsValidation.RecordingTransaction tx = Device.Tracker.BeginRecording(this);
        Device.Tracker.Barrier(this, barrier.Buffer.Buffer, barrier.Before, barrier.After, barrier.Buffer.OffsetInBytes, barrier.Buffer.SizeInBytes);
        Raw.Barrier(native);
        tx.Commit();
    }

    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
        where T : struct, IShaderArguments
    {
        OutsidePass();
        barrier = barrier with { Before = ValidationStages.Normalize(Device, barrier.Before), After = ValidationStages.Normalize(Device, barrier.After) };
        var native = new ShaderDataBufferBarrierDesc<ValidationArguments<T>> { Buffer = Unwrap(barrier.Buffer), Before = barrier.Before, After = barrier.After };
        using GraphicsValidation.RecordingTransaction tx = Device.Tracker.BeginRecording(this);
        Device.Tracker.Barrier(this, barrier.Buffer.Buffer, barrier.Before, barrier.After, checked(barrier.Buffer.Offset * barrier.Buffer.Buffer.ShaderElementStrideInBytes), checked(barrier.Buffer.Count * barrier.Buffer.Buffer.ShaderElementStrideInBytes));
        Raw.Barrier(native);
        tx.Commit();
    }

    public void Barrier(TextureBarrierDesc barrier)
    {
        OutsidePass();
        barrier = barrier with { Before = ValidationStages.Normalize(Device, barrier.Before), After = ValidationStages.Normalize(Device, barrier.After) };
        TextureBarrierDesc native = barrier with { Texture = Device.Unwrap(barrier.Texture) };
        using GraphicsValidation.RecordingTransaction tx = Device.Tracker.BeginRecording(this);
        Device.Tracker.TextureBarrier(this, barrier);
        Raw.Barrier(native);
        tx.Commit();
    }

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc)
    {
        OutsidePass();
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.ColorAttachments);
        if ((uint)desc.ColorAttachments.Count > Device.Caps.MaxColorAttachments)
        {
            throw new ArgumentException("Color attachment count exceeds the enabled device limit.", nameof(desc));
        }

        RenderPassDesc native = desc with { ColorAttachments = desc.ColorAttachments.Select(a => a with { View = Device.Unwrap(a.View) }).ToArray(), DepthStencilAttachment = desc.DepthStencilAttachment is { } depth ? depth with { View = Device.Unwrap(depth.View) } : null };
        using GraphicsValidation.RecordingTransaction tx = Device.Tracker.BeginRecording(this);
        foreach (RenderColorAttachmentDesc attachment in desc.ColorAttachments)
        {
            Attachment(attachment.View, TextureState.ColorAttachment, PipelineStage.ColorOutput, ResourceAccess.ColorWrite | (attachment.LoadOp == AttachmentLoadOp.Load ? ResourceAccess.ColorRead : ResourceAccess.None));
        }

        if (desc.DepthStencilAttachment is { } d)
        {
            Attachment(d.View, TextureState.DepthStencilAttachment, PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite | ((d.DepthLoadOp == AttachmentLoadOp.Load || (d.View.Info.Format == TextureFormat.Depth24Stencil8 && d.StencilLoadOp == AttachmentLoadOp.Load)) ? ResourceAccess.DepthStencilRead : ResourceAccess.None));
        }

        var encoder = new ValidationRenderEncoder(this, Raw.BeginRenderPass(native));
        _pass = encoder;
        tx.Commit();
        return encoder;
    }

    public IComputeEncoder BeginComputePass(ComputePassDesc desc)
    {
        OutsidePass();
        var encoder = new ValidationComputeEncoder(this, Raw.BeginComputePass(desc));
        _pass = encoder;
        return encoder;
    }

    public void Finish()
    {
        OutsidePass();
        Raw.Finish();
    }

    internal void ValidateRecording()
    {
        ValidateAlive();
        if (State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("Command is not recording.");
        }
    }

    internal void ValidatePass(object pass)
    {
        ValidateRecording();
        if (!ReferenceEquals(_pass, pass))
        {
            throw new InvalidOperationException("Pass is no longer active.");
        }
    }

    internal void EndPass(object pass)
    {
        ValidatePass(pass);
        _pass = null;
    }

    internal BufferSlice<T> Unwrap<T>(BufferSlice<T> value)
        where T : unmanaged
        => new(Device.Unwrap(value.Buffer), value.Offset, value.Count);

    internal ShaderDataSlice<ValidationArguments<T>> Unwrap<T>(ShaderDataSlice<T> value)
        where T : struct, IShaderArguments
    {
        Device.UnwrapObject(value.Buffer);
        if (value.Buffer is not ValidationShaderDataBuffer<T> buffer)
        {
            throw new ArgumentException("Invalid shader data buffer.");
        }

        return new(buffer.Raw, value.Offset, value.Count);
    }

    internal IShaderReferenceMetadata ReadShaderDependencies(IShaderDataSource source)
    {
        Device.UnwrapObject(source);
        IShaderReferenceMetadata metadata = _overlay.TryGetValue(source, out IShaderReferenceMetadata? current) ? current : source.TryGetDependencies(out IShaderReferenceMetadata? found) ? found : throw new NotSupportedException("Shader dependencies are unavailable.");
        Device.Tracker.Metadata(this, source, metadata, false);
        return metadata;
    }

    internal void PrepareSubmit()
    {
        ValidateAlive();
        if (_submitted || State != CommandBufferState.Executable)
        {
            throw new InvalidOperationException("Command is not executable.");
        }
    }

    internal void MarkSubmitted()
    {
        _submitted = true;
        Device.Tracker.Submit(this);
    }

    internal void ValidateSubmit() => PrepareSubmit();

    internal void OnSubmitted()
    {
        MarkSubmitted();
        PublishDependencies();
    }

    internal void PublishDependencies()
    {
        foreach (KeyValuePair<IShaderDataSource, Action<IShaderReferenceMetadata>> pair in _publish)
        {
            pair.Value(_overlay[pair.Key]);
        }
    }

    internal void OnCompleted(bool success)
    {
        Device.Tracker.Complete(this, success);
        _overlay.Clear();
        _publish.Clear();
        _submitted = false;
    }

    internal void Complete(bool success) => OnCompleted(success);

    protected override void DisposeNative()
    {
        if (_submitted)
        {
            throw new InvalidOperationException("Command is in flight.");
        }

        Raw.Dispose();
        _preparations.Clear();
        Device.Tracker.Discard(this);
        _overlay.Clear();
        _publish.Clear();
    }

    private void OutsidePass(bool preparing = false)
    {
        if (!preparing)
        {
            _recordingStarted = true;
        }

        if (_preparing && !preparing)
        {
            throw new InvalidOperationException("Preparation is active.");
        }

        ValidateRecording();
        if (_pass != null)
        {
            throw new InvalidOperationException("A pass is active.");
        }
    }

    private ulong BufferTextureFootprint(BufferTextureCopyLayout layout, TextureCopyRegion region)
    {
        ArgumentOutOfRangeException.ThrowIfZero(region.Width);
        ArgumentOutOfRangeException.ThrowIfZero(region.Height);
        ArgumentOutOfRangeException.ThrowIfZero(region.ArrayLayerCount);
        ulong bytesPerTexel = Device.GetTextureCopyLayout(region.Texture.Format).BytesPerTexel;
        return checked((((((ulong)region.ArrayLayerCount - 1) * layout.RowsPerImage) + (region.Height - 1)) * layout.BytesPerRow) + ((ulong)region.Width * bytesPerTexel));
    }

    private TextureCopyRegion Unwrap(TextureCopyRegion value) => value with { Texture = Device.Unwrap(value.Texture) };

    private BufferTextureCopyLayout Unwrap(BufferTextureCopyLayout value) => value with { Buffer = Unwrap(value.Buffer) };

    private void TextureCopyAccess(TextureCopyRegion region, bool write) => Device.Tracker.TextureAccess(this, region.Texture, new(region.MipLevel, 1, region.BaseArrayLayer, region.ArrayLayerCount), write ? TextureState.CopyDestination : TextureState.CopySource, new(PipelineStage.Copy, write ? ResourceAccess.CopyWrite : ResourceAccess.CopyRead));

    private void Attachment(IGraphicsTextureView view, TextureState state, PipelineStage stage, ResourceAccess access)
    {
        TextureViewInfo info = view.Info;
        Device.Tracker.Use(this, view);
        Device.Tracker.TextureAccess(this, view.Texture, new(info.BaseMipLevel, info.MipLevelCount, info.BaseArrayLayer, info.ArrayLayerCount), state, new(stage, access));
    }
}
