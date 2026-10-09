using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Abstractions;

/// <summary>
/// Describes a non-owning element range; default is invalid and does not extend buffer lifetime.
/// </summary>
/// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
public readonly struct BufferSlice<T>
    where T : unmanaged
{
    /// <summary>Initializes a new instance of the <see cref="BufferSlice{T}"/> struct and validates its non-owning range.</summary>
    /// <param name="buffer">The owning allocation.</param>
    /// <param name="offset">The start offset in elements.</param>
    /// <param name="count">The positive element count.</param>
    public BufferSlice(IGraphicsBuffer<T> buffer, ulong offset, ulong count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (count == 0 || offset > buffer.Count || count > buffer.Count - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        buffer.ValidateRange(buffer.Layout.GetSizeInBytes(offset), buffer.Layout.GetSizeInBytes(count));
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

    /// <summary>
    /// Copies source elements into this mapped Upload range; remaining elements are unchanged.
    /// </summary>
    /// <param name="source">The elements to copy; their count must not exceed the Upload range.</param>
    public void CopyFrom(ReadOnlySpan<T> source)
    {
        GetLayout();
        if ((ulong)source.Length > Count)
        {
            throw new ArgumentException("Source does not fit the buffer slice.", nameof(source));
        }

        Buffer.CopyFrom(MemoryMarshal.AsBytes(source), OffsetInBytes, SizeInBytes);
    }

    /// <summary>
    /// Copies this mapped Readback range into caller storage; does not wait or submit.
    /// </summary>
    /// <param name="destination">The caller storage, which must fit the complete Readback range.</param>
    public void CopyTo(Span<T> destination)
    {
        GetLayout();
        if ((ulong)destination.Length < Count)
        {
            throw new ArgumentException("Destination does not fit the complete buffer slice.", nameof(destination));
        }

        Buffer.CopyTo(MemoryMarshal.AsBytes(destination[..checked((int)Count)]), OffsetInBytes, SizeInBytes);
    }

    private BufferLayout<T> GetLayout() => Buffer?.Layout ?? throw new ArgumentException("Invalid buffer slice.");
}
