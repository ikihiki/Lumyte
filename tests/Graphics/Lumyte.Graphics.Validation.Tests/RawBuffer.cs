using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class RawBuffer<T>(BufferDesc<T> desc) : IGraphicsBuffer<T>
    where T : unmanaged
{
    private readonly T[] _data = new T[checked((int)desc.Count)];

    public Action? OnWrite { get; set; }

    public int Writes { get; private set; }

    public int Maps { get; private set; }

    public int Disposals { get; private set; }

    public ulong SizeInBytes => Layout.GetSizeInBytes(Count);

    public BufferLayout<T> Layout { get; } = new((ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), (ulong)System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), 1, 1);

    public ulong Count => desc.Count;

    public BufferUsage Usage => desc.Usage;

    public MemoryPreference Memory => desc.Memory;

    public bool IsMapped { get; private set; }

    public void ValidateByteRange(ulong offset, ulong length)
    {
        if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
    }

    public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
    {
        Writes++;
        OnWrite?.Invoke();
        source.CopyTo(MemoryMarshal.AsBytes(_data.AsSpan()).Slice(checked((int)offset), checked((int)length)));
    }

    public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => MemoryMarshal.AsBytes(_data.AsSpan()).Slice(checked((int)offset), checked((int)length)).CopyTo(destination);

    public ValueTask MapAsync(CancellationToken cancellationToken = default)
    {
        Maps++;
        IsMapped = true;
        return ValueTask.CompletedTask;
    }

    public void Unmap() => IsMapped = false;

    public BufferSlice<T> SliceElements(ulong offset, ulong count) => new(this, offset, count);

    public void CopyFrom(ReadOnlySpan<T> source)
    {
        Writes++;
        OnWrite?.Invoke();
        source.CopyTo(_data);
    }

    public void CopyTo(Span<T> destination) => _data.AsSpan().CopyTo(destination);

    public void Dispose() => Disposals++;
}
