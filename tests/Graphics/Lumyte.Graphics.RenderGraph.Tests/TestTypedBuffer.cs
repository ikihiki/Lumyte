namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class TestTypedBuffer<T>(BufferDesc<T> desc, List<string> lifetime) : IGraphicsBuffer<T>
    where T : unmanaged
{
    public ulong SizeInBytes => Layout.GetSizeInBytes(Count);

    public BufferLayout<T> Layout
    {
        get
        {
            ulong size = (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
            return new(size, size, 1, 1);
        }
    }

    public ulong Count => desc.Count;

    public BufferUsage Usage => desc.Usage;

    public MemoryPreference Memory => desc.Memory;

    public bool IsMapped => false;

    public BufferSlice<T> Slice(ulong offset, ulong count) => new(this, offset, count);

    public void ValidateRange(ulong offset, ulong length)
    {
        if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
    }

    public ValueTask MapAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void Unmap() => throw new NotSupportedException();

    public void CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length) => throw new NotSupportedException();

    public void CopyTo(Span<byte> destination, ulong offset, ulong length) => throw new NotSupportedException();

    public void CopyFrom(ReadOnlySpan<T> source) => throw new NotSupportedException();

    public void CopyTo(Span<T> destination) => throw new NotSupportedException();

    public void Dispose() => lifetime.Add("buffer");
}
