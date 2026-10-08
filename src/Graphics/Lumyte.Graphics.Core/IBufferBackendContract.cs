namespace Lumyte.Graphics;

// One backend buffer instance owns its allocation and CPU memory access.
// Commands, submission and barriers belong to the command backend.

/// <summary>
/// Provides allocation validation and CPU memory access on the concrete backend buffer.
/// </summary>
internal interface IBufferBackendContract : IDisposable
{
    /// <summary>
    /// Gets the native allocation size in bytes.
    /// </summary>
    ulong SizeInBytes { get; }

    /// <summary>
    /// Rejects empty, overflowing, or out-of-bounds byte ranges.
    /// </summary>
    /// <param name="offset">The start offset in elements, or bytes for the backend byte-range contract.</param>
    /// <param name="length">The byte length of the backend range.</param>
    void ValidateRange(ulong offset, ulong length);

    /// <summary>
    /// Copies caller bytes into idle Upload storage without GPU transfer.
    /// </summary>
    /// <param name="source">The caller-owned data or GPU source range.</param>
    /// <param name="offset">The start offset in elements, or bytes for the backend byte-range contract.</param>
    /// <param name="length">The byte length of the backend range.</param>
    void CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length);

    /// <summary>
    /// Copies completed Readback bytes into caller storage without waiting.
    /// </summary>
    /// <param name="destination">The caller-owned destination storage or GPU range.</param>
    /// <param name="offset">The start offset in elements, or bytes for the backend byte-range contract.</param>
    /// <param name="length">The byte length of the backend range.</param>
    void CopyTo(Span<byte> destination, ulong offset, ulong length);
}
