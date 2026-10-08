using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

// The public typed interface and the internal allocation contract are the same object.
internal sealed class WgpuTypedBuffer<T> : WgpuBuffer, IGraphicsBuffer<T>
    where T : unmanaged
{
    internal WgpuTypedBuffer(WgpuDevice owner, A.Buffer native, BufferDesc<T> desc, BufferLayout<T> layout)
        : base(owner, native, layout.GetSizeInBytes(desc.Count), desc.Usage, desc.Memory)
    {
        (Count, Layout) = (desc.Count, layout);
    }

    public ulong Count { get; }

    public BufferLayout<T> Layout { get; }

    public new Lumyte.Graphics.BufferSlice<T> Slice(ulong offset, ulong count)
    {
        if (count == 0 || offset > Count || count > Count - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        ValidateRange(Layout.GetSizeInBytes(offset), Layout.GetSizeInBytes(count));
        return new(this, offset, count);
    }

    public void CopyFrom(ReadOnlySpan<T> source) => CopyFrom(System.Runtime.InteropServices.MemoryMarshal.AsBytes(source), 0, SizeInBytes);

    public void CopyTo(Span<T> destination) => CopyTo(System.Runtime.InteropServices.MemoryMarshal.AsBytes(destination), 0, SizeInBytes);
}
