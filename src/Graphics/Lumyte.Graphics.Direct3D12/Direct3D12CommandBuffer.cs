using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12CommandBuffer : IGraphicsCommandBuffer
{
    internal Direct3D12CommandBuffer(Direct3D12Device owner)
    {
        Owner = owner;
        owner.Check(NativeMethods.CommandsCreate(owner.Native, out nint native));
        Native = native;
    }

    public CommandBufferState State { get; private set; }

    internal Direct3D12Device Owner { get; }

    internal nint Native { get; }

    public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        RequireRecording();
        Direct3D12Buffer<TSource> src = Own(source);
        Direct3D12Buffer<TDestination> dst = Own(destination);
        if ((src.Usage & BufferUsage.CopySource) == 0 || (dst.Usage & BufferUsage.CopyDestination) == 0 || source.SizeInBytes != destination.SizeInBytes || ReferenceEquals(src, dst))
        {
            throw new ArgumentException("Copy requires distinct buffers, matching byte lengths and copy usages.");
        }

        NativeMethods.CommandsCopy(Native, src.Native, source.OffsetInBytes, dst.Native, destination.OffsetInBytes, source.SizeInBytes);
    }

    public void Barrier<T>(BufferBarrierDesc<T> barrier)
        where T : unmanaged
    {
        RequireRecording();
        ArgumentNullException.ThrowIfNull(barrier);
        Direct3D12Buffer<T> buffer = Own(barrier.Buffer);
        uint before = ResolveState(barrier.Before, buffer.Memory, buffer.Usage);
        uint after = ResolveState(barrier.After, buffer.Memory, buffer.Usage);

        // Legacy D3D12 buffer barriers cover the whole resource, never just a byte range.
        if (barrier.Buffer.OffsetInBytes != 0 || barrier.Buffer.SizeInBytes != buffer.SizeInBytes)
        {
            throw new NotSupportedException("Direct3D 12 transfer barriers require the whole buffer.");
        }

        NativeMethods.CommandsBarrier(Native, buffer.Native, before, after);
    }

    public void Finish()
    {
        RequireRecording();
        Owner.Check(NativeMethods.CommandsFinish(Native));
        State = CommandBufferState.Executable;
    }

    public void Dispose()
    {
        if (State == CommandBufferState.Disposed)
        {
            return;
        }

        NativeMethods.CommandsDestroy(Native);
        State = CommandBufferState.Disposed;
    }

    public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => throw Unsupported();

    public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => throw Unsupported();

    public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => throw Unsupported();

    public void Barrier(MemoryBarrierDesc barrier) => throw Unsupported();

    public void Barrier(TextureBarrierDesc barrier) => throw Unsupported();

    public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => throw Unsupported();

    public IComputeEncoder BeginComputePass(ComputePassDesc desc) => throw Unsupported();

    public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
        where T : struct, IShaderData => throw Unsupported();

    public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
        where T : struct, IShaderData => throw Unsupported();

    internal void MarkSubmitted() => State = CommandBufferState.Submitted;

    internal void Complete(bool success)
    {
        if (State != CommandBufferState.Disposed)
        {
            State = success ? CommandBufferState.Completed : CommandBufferState.Faulted;
        }
    }

    private static uint ResolveState(BarrierScope scope, MemoryPreference memory, BufferUsage usage)
    {
        ResourceAccess access = scope.Access;
        if ((scope.Stages & ~(PipelineStage.Host | PipelineStage.Copy)) != 0 ||
            (access != ResourceAccess.None && access != ResourceAccess.HostRead && access != ResourceAccess.HostWrite && access != ResourceAccess.CopyRead && access != ResourceAccess.CopyWrite) ||
            ((access is ResourceAccess.CopyRead or ResourceAccess.CopyWrite) && (scope.Stages & PipelineStage.Copy) == 0) ||
            ((access is ResourceAccess.HostRead or ResourceAccess.HostWrite) && (scope.Stages & PipelineStage.Host) == 0))
        {
            throw new NotSupportedException("Only host and copy buffer scopes are supported.");
        }

        if ((access == ResourceAccess.CopyRead && (usage & BufferUsage.CopySource) == 0) ||
            (access == ResourceAccess.CopyWrite && (usage & BufferUsage.CopyDestination) == 0))
        {
            throw new ArgumentException("Barrier access is not allowed by the buffer usage.");
        }

        return memory switch
        {
            MemoryPreference.Upload when access is ResourceAccess.None or ResourceAccess.HostWrite or ResourceAccess.CopyRead => 0xAC3,
            MemoryPreference.Readback when access is ResourceAccess.None or ResourceAccess.HostRead or ResourceAccess.CopyWrite => 0x400,
            MemoryPreference.Automatic when access == ResourceAccess.None => 0,
            MemoryPreference.Automatic when access == ResourceAccess.CopyRead => 0x800,
            MemoryPreference.Automatic when access == ResourceAccess.CopyWrite => 0x400,
            _ => throw new NotSupportedException("The access is incompatible with the heap's fixed resource state."),
        };
    }

    private Direct3D12Buffer<T> Own<T>(BufferSlice<T> slice)
        where T : unmanaged
    {
        if (slice.Buffer is not Direct3D12Buffer<T> buffer || !ReferenceEquals(buffer.Owner, Owner))
        {
            throw new ArgumentException("Buffer belongs to another device.", nameof(slice));
        }

        buffer.ValidateByteRange(slice.OffsetInBytes, slice.SizeInBytes);
        _ = buffer.Native;
        return buffer;
    }

    private void RequireRecording()
    {
        Owner.RequireReady();
        ObjectDisposedException.ThrowIf(State == CommandBufferState.Disposed, this);
        if (State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("Commands must be in Recording state.");
        }
    }

    private NotSupportedException Unsupported()
    {
        RequireRecording();
        return new("Only buffer copies and explicit whole-buffer barriers are supported.");
    }
}
