using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe class Direct3D12Buffer<T> : IGraphicsBuffer<T>, IShaderRawBuffer
    where T : unmanaged
{
    private readonly nint _native;
    private nint _pointer;
    private bool _disposed;

    internal Direct3D12Buffer(Direct3D12Device owner, BufferDesc<T> desc, BufferLayout<T> layout, ulong size)
    {
        (Owner, Layout, Count, SizeInBytes, Usage, Memory) = (owner, layout, desc.Count, size, desc.Usage, desc.Memory);
        owner.Check(NativeMethods.BufferCreate(owner.Native, size, Memory == MemoryPreference.Upload ? 1 : Memory == MemoryPreference.Readback ? 2 : 0, (Usage & BufferUsage.ShaderWrite) != 0 ? 1 : 0, out _native));
    }

    public BufferLayout<T> Layout { get; }

    public ulong Count { get; }

    public ulong SizeInBytes { get; }

    public BufferUsage Usage { get; }

    public MemoryPreference Memory { get; }

    public bool IsMapped => _pointer != 0;

    public object ShaderHandle => Native;

    internal Direct3D12Device Owner { get; }

    internal nint Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Owner.RequireReady();
            if (IsMapped)
            {
                throw new InvalidOperationException("GPU access requires an unmapped buffer.");
            }

            return _native;
        }
    }

    public BufferSlice<T> SliceElements(ulong offset, ulong count) => new(this, offset, count);

    public void CopyFrom(ReadOnlySpan<T> source) => SliceElements(0, Count).CopyFrom(source);

    public void CopyTo(Span<T> destination) => SliceElements(0, Count).CopyTo(destination);

    public void ValidateByteRange(ulong offset, ulong length)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }
    }

    public ValueTask MapAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Owner.RequireReady();
        cancellationToken.ThrowIfCancellationRequested();
        if (IsMapped || Memory == MemoryPreference.Automatic)
        {
            throw new InvalidOperationException("This buffer cannot be mapped in its current state.");
        }

        Owner.Check(NativeMethods.BufferMap(_native, SizeInBytes, Memory == MemoryPreference.Readback ? 1 : 0, out _pointer));
        return ValueTask.CompletedTask;
    }

    public void Unmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsMapped)
        {
            throw new InvalidOperationException("No CPU mapping is active.");
        }

        NativeMethods.BufferUnmap(_native, SizeInBytes, Memory == MemoryPreference.Upload ? 1 : 0);
        _pointer = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (IsMapped)
        {
            Unmap();
        }

        NativeMethods.Release(_native);
        _disposed = true;
    }

    void IGraphicsBuffer<T>.CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
    {
        ValidateByteRange(offset, length);
        RequireMapping(MemoryPreference.Upload);
        if ((ulong)source.Length > length)
        {
            throw new ArgumentException("Source exceeds the selected range.", nameof(source));
        }

        source.CopyTo(new Span<byte>((byte*)_pointer + checked((int)offset), source.Length));
    }

    void IGraphicsBuffer<T>.CopyBytesTo(Span<byte> destination, ulong offset, ulong length)
    {
        ValidateByteRange(offset, length);
        RequireMapping(MemoryPreference.Readback);
        if ((ulong)destination.Length < length)
        {
            throw new ArgumentException("Destination is smaller than the selected range.", nameof(destination));
        }

        new ReadOnlySpan<byte>((byte*)_pointer + checked((int)offset), checked((int)length)).CopyTo(destination);
    }

    private void RequireMapping(MemoryPreference memory)
    {
        Owner.RequireReady();
        if (!IsMapped || Memory != memory)
        {
            throw new InvalidOperationException("The requested CPU access requires the matching mapped heap.");
        }
    }
}
