using System.Runtime.InteropServices;

namespace Lumyte.Graphics;

/// <summary>
/// Describes a non-owning element range; default is invalid and does not extend buffer lifetime.
/// </summary>
/// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
public readonly struct BufferSlice<T>
    where T : unmanaged
{
    internal BufferSlice(IGraphicsBuffer<T> buffer, ulong offset, ulong count)
    {
        (Buffer, Offset, Count) = (buffer, offset, count);
    }

    /// <summary>
    /// Gets the owning buffer for this range.
    /// </summary>
    public IGraphicsBuffer<T> Buffer { get; }

    /// <summary>
    /// Gets the start offset in elements.
    /// </summary>
    public ulong Offset { get; }

    /// <summary>
    /// Gets the range count in elements.
    /// </summary>
    public ulong Count { get; }

    /// <summary>
    /// Gets the checked start offset in backend storage bytes.
    /// </summary>
    public ulong OffsetInBytes => GetLayout().GetSizeInBytes(Offset);

    /// <summary>
    /// Gets the checked range size in backend storage bytes.
    /// </summary>
    public ulong SizeInBytes => GetLayout().GetSizeInBytes(Count);

    internal BufferRange Range => Buffer is IBufferBackendContract backend ? new(backend, OffsetInBytes, SizeInBytes) : throw new ArgumentException("Invalid buffer slice or unsupported implementation.");

    /// <summary>
    /// Copies source elements into this idle Upload range; remaining elements are unchanged.
    /// </summary>
    /// <param name="source">The elements to copy; their count must not exceed the Upload range.</param>
    public void CopyFrom(ReadOnlySpan<T> source)
    {
        BufferRange range = Range;
        range.Buffer.CopyFrom(MemoryMarshal.AsBytes(source), range.Offset, range.Length);
    }

    /// <summary>
    /// Copies this completed, idle Readback range into caller storage; does not wait or submit.
    /// </summary>
    /// <param name="destination">The caller storage, which must fit the complete Readback range.</param>
    public void CopyTo(Span<T> destination)
    {
        BufferRange range = Range;
        range.Buffer.CopyTo(MemoryMarshal.AsBytes(destination), range.Offset, range.Length);
    }

    private BufferLayout<T> GetLayout() => Buffer?.Layout ?? throw new ArgumentException("Invalid buffer slice.");
}
