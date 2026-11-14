using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe partial class Direct3D12CommandBuffer : IGraphicsCommandBuffer
{
    private readonly CommandDependencyState? _shaderDependencies;
    private readonly List<IDisposable> _bindings = [];
    private readonly Direct3D12Device _owner;
    private object? _active;

    internal Direct3D12CommandBuffer(Direct3D12Device owner)
    {
        _owner = owner;
        if (owner.Validation != null)
        {
            _shaderDependencies = new CommandDependencyState(owner.Validation, this);
        }

        Initialize();
    }

    public CommandBufferState State { get; private set; } = CommandBufferState.Recording;

    internal Direct3D12Device Owner => _owner;

    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
        where T : struct, IShaderArguments
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = _owner.Validation?.BeginRecording(this);
        RequireRecording();
        if (source.Buffer is not Direct3D12ShaderDataBuffer<T> src || destination.Buffer is not Direct3D12ShaderDataBuffer<T> dst || !ReferenceEquals(src.Owner, _owner) || !ReferenceEquals(dst.Owner, _owner) || source.Count != destination.Count || dst.Memory != MemoryPreference.Automatic)
        {
            throw new ArgumentException("Shader data copy requires equal ranges from this device.");
        }

        src.ValidateAlive();
        dst.ValidateAlive();
        IShaderReferenceMetadata? copied = _shaderDependencies?.PrepareCopy(src, dst, destination.Offset == 0 && destination.Count == dst.Count);
        CopyBuffer(src.Storage.SliceElements(checked(source.Offset * src.ShaderElementStrideInBytes), checked(source.Count * src.ShaderElementStrideInBytes)), dst.Storage.SliceElements(checked(destination.Offset * dst.ShaderElementStrideInBytes), checked(destination.Count * dst.ShaderElementStrideInBytes)));
        if (copied != null)
        {
            _shaderDependencies!.InstallCopy(dst, copied);
        }

        validationRecording?.Commit();
    }

    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
        where T : struct, IShaderArguments
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = _owner.Validation?.BeginRecording(this);
        RequireRecording();
        ArgumentNullException.ThrowIfNull(barrier);
        if (barrier.Buffer.Buffer is not Direct3D12ShaderDataBuffer<T> buffer || !ReferenceEquals(buffer.Owner, _owner))
        {
            throw new ArgumentException("Shader data barrier belongs to another device.");
        }

        Barrier(new BufferBarrierDesc<byte> { Buffer = buffer.Storage.SliceElements(checked(barrier.Buffer.Offset * buffer.ShaderElementStrideInBytes), checked(barrier.Buffer.Count * buffer.ShaderElementStrideInBytes)), Before = barrier.Before, After = barrier.After });
        validationRecording?.Commit();
    }

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = _owner.Validation?.BeginRecording(this);
        RequireRecording();
        Direct3D12Buffer<TSource> src = Buffer(source, BufferUsage.CopySource);
        Direct3D12Buffer<TDestination> dst = Buffer(destination, BufferUsage.CopyDestination);
        if (ReferenceEquals(src, dst) || source.SizeInBytes != destination.SizeInBytes || source.OffsetInBytes % src.Layout.CopyOffsetAlignmentInBytes != 0 ||
            destination.OffsetInBytes % dst.Layout.CopyOffsetAlignmentInBytes != 0 || source.SizeInBytes % src.Layout.CopySizeAlignmentInBytes != 0 || destination.SizeInBytes % dst.Layout.CopySizeAlignmentInBytes != 0)
        {
            throw new ArgumentException("Invalid GPU copy range, allocation or alignment.");
        }

        _owner.Validation?.Access(this, src, new(PipelineStage.Copy, ResourceAccess.CopyRead), source.OffsetInBytes, source.SizeInBytes);
        _owner.Validation?.Access(this, dst, new(PipelineStage.Copy, ResourceAccess.CopyWrite), destination.OffsetInBytes, destination.SizeInBytes);
        CopyBufferNative(src, source.OffsetInBytes, dst, destination.OffsetInBytes, source.SizeInBytes);
        validationRecording?.Commit();
    }

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination)
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = _owner.Validation?.BeginRecording(this);
        RequireRecording();
        Direct3D12Texture src = Texture(source, TextureUsage.CopySource);
        Direct3D12Texture dst = Texture(destination, TextureUsage.CopyDestination);
        if (ReferenceEquals(src, dst) || source.Texture.Format != destination.Texture.Format || source.Width != destination.Width || source.Height != destination.Height || source.ArrayLayerCount != destination.ArrayLayerCount)
        {
            throw new ArgumentException("Texture copies require distinct allocations and equal format and extent.");
        }

        _owner.Validation?.TextureAccess(this, src, new(source.MipLevel, 1, source.BaseArrayLayer, source.ArrayLayerCount), TextureState.CopySource, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        _owner.Validation?.TextureAccess(this, dst, new(destination.MipLevel, 1, destination.BaseArrayLayer, destination.ArrayLayerCount), TextureState.CopyDestination, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        CopyTextureNative(source, destination);
        validationRecording?.Commit();
    }

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination)
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = _owner.Validation?.BeginRecording(this);
        RequireRecording();
        Direct3D12Texture texture = Texture(destination, TextureUsage.CopyDestination);
        ArgumentNullException.ThrowIfNull(source);
        _ = Buffer(source.Buffer, BufferUsage.CopySource);
        CommandValidation.Layout(source, destination, _owner.GetTextureCopyLayout(texture.Format));
        _owner.Validation?.Access(this, source.Buffer.Buffer, new(PipelineStage.Copy, ResourceAccess.CopyRead), source.Buffer.OffsetInBytes, checked((((ulong)destination.ArrayLayerCount - 1) * source.RowsPerImage * source.BytesPerRow) + (((ulong)destination.Height - 1) * source.BytesPerRow) + ((ulong)destination.Width * _owner.GetTextureCopyLayout(texture.Format).BytesPerTexel)));
        _owner.Validation?.TextureAccess(this, texture, new(destination.MipLevel, 1, destination.BaseArrayLayer, destination.ArrayLayerCount), TextureState.CopyDestination, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        CopyBufferTextureNative(source, destination, true);
        validationRecording?.Commit();
    }

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination)
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = _owner.Validation?.BeginRecording(this);
        RequireRecording();
        Direct3D12Texture texture = Texture(source, TextureUsage.CopySource);
        ArgumentNullException.ThrowIfNull(destination);
        _ = Buffer(destination.Buffer, BufferUsage.CopyDestination);
        CommandValidation.Layout(destination, source, _owner.GetTextureCopyLayout(texture.Format));
        _owner.Validation?.TextureAccess(this, texture, new(source.MipLevel, 1, source.BaseArrayLayer, source.ArrayLayerCount), TextureState.CopySource, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        _owner.Validation?.Access(this, destination.Buffer.Buffer, new(PipelineStage.Copy, ResourceAccess.CopyWrite), destination.Buffer.OffsetInBytes, checked((((ulong)source.ArrayLayerCount - 1) * destination.RowsPerImage * destination.BytesPerRow) + (((ulong)source.Height - 1) * destination.BytesPerRow) + ((ulong)source.Width * _owner.GetTextureCopyLayout(texture.Format).BytesPerTexel)));
        CopyBufferTextureNative(destination, source, false);
        validationRecording?.Commit();
    }

    public void Barrier(MemoryBarrierDesc barrier)
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = _owner.Validation?.BeginRecording(this);
        RequireRecording();
        ArgumentNullException.ThrowIfNull(barrier);
        CommandValidation.Scope(barrier.Before);
        CommandValidation.Scope(barrier.After);
        _owner.Validation?.MemoryBarrier(this, barrier.Before, barrier.After);
        MemoryBarrierNative(barrier);
        validationRecording?.Commit();
    }

    public void Barrier<T>(BufferBarrierDesc<T> barrier)
        where T : unmanaged
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = _owner.Validation?.BeginRecording(this);
        RequireRecording();
        ArgumentNullException.ThrowIfNull(barrier);
        Direct3D12Buffer<T> buffer = Buffer(barrier.Buffer, 0);
        CommandValidation.Scope(barrier.Before);
        CommandValidation.Scope(barrier.After);
        CommandValidation.BufferAccess(buffer, barrier.Before);
        CommandValidation.BufferAccess(buffer, barrier.After);
        _owner.Validation?.Barrier(this, buffer, barrier.Before, barrier.After, barrier.Buffer.OffsetInBytes, barrier.Buffer.SizeInBytes);
        BufferBarrierNative(buffer, barrier);
        validationRecording?.Commit();
    }

    public void Barrier(TextureBarrierDesc barrier)
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = _owner.Validation?.BeginRecording(this);
        RequireRecording();
        ArgumentNullException.ThrowIfNull(barrier);
        Direct3D12Texture texture = OwnTexture(barrier.Texture);
        CommandValidation.Range(texture, barrier.Range);
        CommandValidation.TextureState(texture, barrier.BeforeState, false);
        CommandValidation.TextureState(texture, barrier.AfterState, true);
        CommandValidation.Scope(barrier.Before);
        CommandValidation.Scope(barrier.After);
        CommandValidation.TextureAccess(barrier.BeforeState, barrier.Before);
        CommandValidation.TextureAccess(barrier.AfterState, barrier.After);
        _owner.Validation?.TextureBarrier(this, barrier);
        TextureBarrierNative(texture, barrier);
        validationRecording?.Commit();
    }

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc)
    {
        using GraphicsValidation.RecordingTransaction? validationRecording = _owner.Validation?.BeginRecording(this);
        RequireRecording();
        ArgumentNullException.ThrowIfNull(desc);
        ArgumentNullException.ThrowIfNull(desc.ColorAttachments);
        RenderColorAttachmentDesc[] attachments = desc.ColorAttachments.ToArray();
        if ((attachments.Length == 0 && desc.DepthStencilAttachment == null) || (uint)attachments.Length > _owner.Caps.MaxColorAttachments)
        {
            throw new ArgumentException("Invalid color attachment count.");
        }

        (uint Width, uint Height)? size = null;
        foreach (RenderColorAttachmentDesc attachment in attachments)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            if (attachment.View is not Direct3D12TextureView view || !ReferenceEquals(view.Owner, _owner))
            {
                throw new ArgumentException("Attachment belongs to another device.");
            }

            _ = view.Native;
            TextureViewInfo info = view.Info;
            Direct3D12Texture texture = OwnTexture(view.Texture);
            (uint width, uint height) = texture.GetMipSize(info.BaseMipLevel);
            ClearColor clear = attachment.ClearValue;
            if (info.Format is TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8 || (texture.Usage & TextureUsage.RenderAttachment) == 0 || info.Dimension != TextureViewDimension.D2 || info.MipLevelCount != 1 || info.ArrayLayerCount != 1 ||
                (size is { } expected && expected != (width, height)) ||
                !Enum.IsDefined(attachment.LoadOp) || !Enum.IsDefined(attachment.StoreOp) || !double.IsFinite(clear.Red) || !double.IsFinite(clear.Green) || !double.IsFinite(clear.Blue) || !double.IsFinite(clear.Alpha))
            {
                throw new ArgumentException("Invalid color attachment, operation or dimensions.");
            }

            _owner.Validation?.Use(this, view);
            size = (width, height);
        }

        RenderDepthStencilAttachmentDesc? depth = desc.DepthStencilAttachment;
        if (depth != null)
        {
            if (depth.View is not Direct3D12TextureView view || !ReferenceEquals(view.Owner, _owner))
            {
                throw new ArgumentException("Depth attachment belongs to another device.");
            }

            _ = view.Native;
            TextureViewInfo info = view.Info;
            Direct3D12Texture texture = OwnTexture(view.Texture);
            (uint width, uint height) = texture.GetMipSize(info.BaseMipLevel);
            if (info.Format is not (TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8) ||
                (texture.Usage & TextureUsage.RenderAttachment) == 0 || info.Dimension != TextureViewDimension.D2 || info.MipLevelCount != 1 || info.ArrayLayerCount != 1 ||
                (size is { } expected && expected != (width, height)) || !Enum.IsDefined(depth.DepthLoadOp) || !Enum.IsDefined(depth.DepthStoreOp) ||
                !Enum.IsDefined(depth.StencilLoadOp) || !Enum.IsDefined(depth.StencilStoreOp) || !float.IsFinite(depth.DepthClearValue) ||
                depth.DepthClearValue < 0 || depth.DepthClearValue > 1 || depth.StencilClearValue > 255)
            {
                throw new ArgumentException("Invalid depth/stencil attachment, operation or dimensions.");
            }
        }

        if (_owner.Validation is { } validation)
        {
            foreach (RenderColorAttachmentDesc attachment in attachments)
            {
                TextureViewInfo info = attachment.View.Info;
                validation.Use(this, attachment.View);
                validation.TextureAccess(this, attachment.View.Texture, new(info.BaseMipLevel, info.MipLevelCount, info.BaseArrayLayer, info.ArrayLayerCount), TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite | (attachment.LoadOp == AttachmentLoadOp.Load ? ResourceAccess.ColorRead : 0)));
            }

            if (depth != null)
            {
                TextureViewInfo info = depth.View.Info;
                validation.Use(this, depth.View);
                validation.TextureAccess(this, depth.View.Texture, new(info.BaseMipLevel, info.MipLevelCount, info.BaseArrayLayer, info.ArrayLayerCount), TextureState.DepthStencilAttachment, new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite | ResourceAccess.DepthStencilRead));
            }
        }

        IRenderEncoder pass = BeginRenderNative(attachments, depth);
        validationRecording?.Commit();
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

        if (_active != null && _owner.Status == GraphicsDeviceStatus.Ready)
        {
            throw new InvalidOperationException("End the pass before disposal.");
        }

        _active = null;

        _owner.Validation?.Discard(this);
        _owner.Validation?.Release(this);
        DisposeNative();
        foreach (IDisposable binding in _bindings)
        {
            binding.Dispose();
        }

        _bindings.Clear();
        State = CommandBufferState.Disposed;
    }

    internal IShaderReferenceMetadata? ReadShaderDependencies(IShaderDataSource source) => _shaderDependencies?.Read(source);

    internal void ValidateSubmit()
    {
        if (_owner.Validation != null)
        {
            _shaderDependencies!.ValidateSubmit();
        }

        if (State != CommandBufferState.Executable)
        {
            throw new InvalidOperationException("Only executable, never-submitted commands may be submitted.");
        }
    }

    internal void MarkSubmitted()
    {
        _owner.Validation?.Submit(this);
        _shaderDependencies?.Publish();
        State = CommandBufferState.Submitted;
    }

    internal void Complete(bool success)
    {
        _owner.Validation?.Complete(this, success);
        if (State != CommandBufferState.Disposed)
        {
            State = success ? CommandBufferState.Completed : CommandBufferState.Faulted;
        }
    }

    internal void KeepBinding(IDisposable binding) => _bindings.Add(binding);

    internal void ValidatePass(object pass) => ValidateEnd(pass);

    internal Direct3D12Buffer<T> Buffer<T>(BufferSlice<T> slice, BufferUsage usage)
        where T : unmanaged
    {
        if (slice.Buffer is not Direct3D12Buffer<T> buffer || !ReferenceEquals(buffer.Owner, _owner) || (buffer.Usage & usage) != usage)
        {
            throw new ArgumentException("Buffer belongs to another device or lacks required usage.");
        }

        buffer.ValidateByteRange(slice.OffsetInBytes, slice.SizeInBytes);
        _ = buffer.Native;
        _owner.Validation?.Use(this, buffer);
        return buffer;
    }

    private void RequireRecording()
    {
        _owner.RequireReady();
        ObjectDisposedException.ThrowIf(State == CommandBufferState.Disposed, this);
        if (State != CommandBufferState.Recording || _active != null)
        {
            throw new InvalidOperationException("Commands require Recording state outside a pass.");
        }
    }

    private Direct3D12Texture OwnTexture(IGraphicsTexture resource)
    {
        if (resource is not Direct3D12Texture texture || !ReferenceEquals(texture.Owner, _owner))
        {
            throw new ArgumentException("Texture belongs to another device.");
        }

        _ = texture.Native;
        _owner.Validation?.Use(this, texture);
        return texture;
    }

    private Direct3D12Texture Texture(TextureCopyRegion region, TextureUsage usage)
    {
        CommandValidation.Region(region, usage);
        return OwnTexture(region.Texture);
    }

    private void ValidateEnd(object pass)
    {
        _owner.RequireReady();
        if (State != CommandBufferState.Recording || !ReferenceEquals(_active, pass))
        {
            throw new InvalidOperationException("This pass is not active.");
        }
    }

    private sealed class CommandDependencyState(GraphicsValidation validation, Direct3D12CommandBuffer command)
    {
        private readonly Dictionary<IShaderDataSource, IShaderReferenceMetadata> _overlay = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<IShaderDataSource, IShaderReferenceMetadata> _captured = new(ReferenceEqualityComparer.Instance);

        internal IShaderReferenceMetadata Read(IShaderDataSource source)
        {
            IShaderReferenceMetadata value;
            if (_overlay.TryGetValue(source, out IShaderReferenceMetadata? pending))
            {
                value = pending;
            }
            else
            {
                if (!source.TryGetDependencies(out IShaderReferenceMetadata? current))
                {
                    throw new NotSupportedException("Shader dependencies unavailable.");
                }

                if (_captured.TryGetValue(source, out IShaderReferenceMetadata? previous) && !ReferenceEquals(previous, current))
                {
                    throw new InvalidOperationException("Shader dependencies changed during command recording.");
                }

                _captured[source] = current;
                value = current;
            }

            validation.Metadata(command, source, value, false);
            return value;
        }

        internal IShaderReferenceMetadata PrepareCopy(IShaderDataSource source, IShaderDataSource destination, bool full)
        {
            IShaderReferenceMetadata copied = Read(source);
            if (full)
            {
                return copied;
            }

            // Partial writes cannot restore an unknown destination.
            return Direct3D12ReferenceMetadata.Merge(Read(destination), copied);
        }

        internal void InstallCopy(IShaderDataSource destination, IShaderReferenceMetadata dependencies)
        {
            validation.Metadata(command, destination, dependencies, true);
            _overlay[destination] = dependencies;
        }

        internal void ValidateSubmit()
        {
            foreach (KeyValuePair<IShaderDataSource, IShaderReferenceMetadata> entry in _captured)
            {
                if (!entry.Key.TryGetDependencies(out IShaderReferenceMetadata? current) || !ReferenceEquals(current, entry.Value))
                {
                    throw new InvalidOperationException("Shader dependencies changed before submission.");
                }
            }
        }

        internal void Publish()
        {
            foreach (KeyValuePair<IShaderDataSource, IShaderReferenceMetadata> entry in _overlay)
            {
                ((IDirect3D12DependencyStorage)entry.Key).PublishDependencies(entry.Value);
            }
        }
    }
}
