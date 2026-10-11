using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class TestBuffer : IGraphicsBuffer<uint>
{
    public ulong SizeInBytes => 64;

    public BufferLayout<uint> Layout => new(4, 4, 1, 1);

    public ulong Count => 16;

    public BufferUsage Usage => BufferUsage.CopySource | BufferUsage.CopyDestination | BufferUsage.ShaderRead | BufferUsage.ShaderWrite;

    public MemoryPreference Memory => MemoryPreference.Automatic;

    public bool IsMapped => false;

    public void ValidateRange(ulong offset, ulong length)
    {
        if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
    }

    public BufferSlice<uint> Slice(ulong offset, ulong count) => new(this, offset, count);

    public ValueTask MapAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void Unmap() => throw new NotSupportedException();

    public void CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length) => throw new NotSupportedException();

    public void CopyTo(Span<byte> destination, ulong offset, ulong length) => throw new NotSupportedException();

    public void CopyFrom(ReadOnlySpan<uint> source) => throw new NotSupportedException();

    public void CopyTo(Span<uint> destination) => throw new NotSupportedException();

    public void Dispose()
    {
    }
}
