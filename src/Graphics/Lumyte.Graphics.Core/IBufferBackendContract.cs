namespace Lumyte.Graphics;

// One backend buffer instance owns its allocation and CPU memory access.
// Commands, submission and barriers belong to the command backend.
internal interface IBufferBackendContract : IDisposable
{
    ulong SizeInBytes { get; }
    void ValidateRange(ulong offset, ulong length);
    void CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length);
    void CopyTo(Span<byte> destination, ulong offset, ulong length);
}
