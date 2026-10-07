namespace Lumyte.Graphics;

// Buffer allocation, ranges and CPU memory copies; no commands, submission or barriers.
internal interface IBufferBackendContract
{
    ulong MaxBufferSize { get; }
    object CreateBuffer(BufferDesc desc);
    object CreateReference<T>(BufferSlice data) where T : unmanaged;
    void CopyBuffer<T>(ReadOnlySpan<T> values, BufferSlice destination) where T : unmanaged;
    void CopyBuffer(BufferSlice source, Span<byte> destination);
    ulong BufferSize(object handle);
    void ValidateSlice(object handle, ulong offset, ulong length);
}
