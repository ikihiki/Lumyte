using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Provides deterministic managed test instrumentation.</summary>
internal sealed class ManagedBuffer<T>(ulong count, List<string> events) : IGraphicsBuffer<T>
    where T : unmanaged
{
    public ulong Count => count;

    public BufferLayout<T> Layout => new((ulong)Unsafe.SizeOf<T>(), (ulong)Unsafe.SizeOf<T>(), 1, 1);

    public ulong SizeInBytes => Layout.GetSizeInBytes(count);

    public BufferUsage Usage => BufferUsage.CopySource | BufferUsage.CopyDestination | BufferUsage.ShaderRead | BufferUsage.ShaderWrite;

    public MemoryPreference Memory => MemoryPreference.Automatic;

    public bool IsMapped => false;

    public void ValidateByteRange(ulong offset, ulong length)
    {
        if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
    }

    public BufferSlice<T> SliceElements(ulong offset, ulong count) => new(this, offset, count);

    public void Dispose() => events.Add("dispose-buffer");

    public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length) => throw new NotSupportedException();

    public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => throw new NotSupportedException();

    public ValueTask MapAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void Unmap() => throw new NotSupportedException();

    public void CopyFrom(ReadOnlySpan<T> source) => throw new NotSupportedException();

    public void CopyTo(Span<T> destination) => throw new NotSupportedException();
}
