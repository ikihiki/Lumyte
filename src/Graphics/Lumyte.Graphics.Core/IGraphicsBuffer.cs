using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics;

/// <summary>A typed GPU allocation implemented directly by its backend; owns no wrapper allocation.</summary>
public interface IGraphicsBuffer<T> : IDisposable where T : unmanaged
{
    ulong Count { get; }
    ulong SizeInBytes { get; }
    BufferUsage Usage { get; }
    MemoryPreference Memory { get; }
    /// <summary>Creates a non-owning value slice. Offset and count are in elements of T.</summary>
    BufferSlice<T> Slice(ulong offset, ulong count);
    /// <summary>Copies values into idle Upload memory, without GPU work or submission.</summary>
    void CopyFrom(ReadOnlySpan<T> source);
    /// <summary>Copies completed Readback memory into caller storage, without GPU completion waits.</summary>
    void CopyTo(Span<T> destination);
}

/// <summary>A non-owning element range; default is invalid and the owner's lifetime is unchanged.</summary>
public readonly struct BufferSlice<T> where T : unmanaged
{
    public IGraphicsBuffer<T> Buffer { get; }
    public ulong Offset { get; }
    public ulong Count { get; }
    public ulong OffsetInBytes => checked(Offset * (ulong)Unsafe.SizeOf<T>());
    public ulong SizeInBytes => checked(Count * (ulong)Unsafe.SizeOf<T>());
    internal BufferRange Range => Buffer is IBufferBackendContract backend
        ? new(backend, OffsetInBytes, SizeInBytes)
        : throw new ArgumentException("Invalid buffer slice or unsupported implementation.");
    internal BufferSlice(IGraphicsBuffer<T> buffer, ulong offset, ulong count)
        => (Buffer, Offset, Count) = (buffer, offset, count);
    public void CopyFrom(ReadOnlySpan<T> source)
    {
        var range = Range;
        range.Buffer.CopyFrom(MemoryMarshal.AsBytes(source), range.Offset, range.Length);
    }
    public void CopyTo(Span<T> destination)
    {
        var range = Range;
        range.Buffer.CopyTo(MemoryMarshal.AsBytes(destination), range.Offset, range.Length);
    }
}

// Byte range shared with command backends, referring directly to the concrete allocation.
internal readonly record struct BufferRange(IBufferBackendContract Buffer, ulong Offset, ulong Length);
