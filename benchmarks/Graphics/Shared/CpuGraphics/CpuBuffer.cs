using System.Runtime.InteropServices;

using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuBuffer<T> : CpuResource, IGraphicsBuffer<T>, IShaderRawBuffer

    where T : unmanaged
{
    internal CpuBuffer(CpuGraphicsDevice owner, BufferDesc<T> desc)
            : base(owner)
    {
        Count = desc.Count;
        Usage = desc.Usage;
        Memory = desc.Memory;
        Layout = owner.GetBufferLayout<T>();
        Bytes = new byte[checked((int)SizeInBytes)];
    }

    public BufferLayout<T> Layout
    {
        get;
    }

    public ulong Count
    {
        get;
    }

    public BufferUsage Usage
    {
        get;
    }

    public MemoryPreference Memory
    {
        get;
    }

    public bool IsMapped
    {
        get;
        private set;
    }

    public object ShaderHandle => this;

    public ulong SizeInBytes => Layout.GetSizeInBytes(Count);

    public Type ElementType => typeof(T);

    public string ShaderTypeName => ShaderTransfer.TypeName<T>();

    internal byte[] Bytes
    {
        get;
    }

    public void ValidateByteRange(ulong offset, ulong length)
    {
        Check();
        if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
    }

    public ValueTask MapAsync(CancellationToken cancellationToken = default)
    {
        Check();
        cancellationToken.ThrowIfCancellationRequested();
        if (Memory == MemoryPreference.Automatic || IsMapped)
        {
            throw new InvalidOperationException("Mapping requires unmapped CPU storage.");
        }

        IsMapped = true;
        return ValueTask.CompletedTask;
    }

    public void Unmap()
    {
        Check();
        if (!IsMapped)
        {
            throw new InvalidOperationException("Buffer is not mapped.");
        }

        IsMapped = false;
    }

    public BufferSlice<T> SliceElements(ulong offset, ulong count)
    {
        Check();
        return new(this, offset, count);
    }

    public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
    {
        ValidateByteRange(offset, length);
        if (Memory != MemoryPreference.Upload || !IsMapped)
        {
            throw new InvalidOperationException("Writes require mapped upload storage.");
        }

        if ((ulong)source.Length > length)
        {
            throw new ArgumentException("Source exceeds range.");
        }

        source.CopyTo(Bytes.AsSpan(checked((int)offset)));
        Owner.Uploads += source.Length;
        Owner.Consume(source);
    }

    public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length) => throw new NotSupportedException("No real GPU execution or pixel/buffer readback is modeled.");

    public void CopyFrom(ReadOnlySpan<T> source) => CopyBytesFrom(MemoryMarshal.AsBytes(source), 0, SizeInBytes);

    public void CopyTo(Span<T> destination) => throw new NotSupportedException("No GPU readback is modeled.");
}
