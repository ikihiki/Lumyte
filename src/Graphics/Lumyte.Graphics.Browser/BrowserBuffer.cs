using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserBuffer<T> : IGraphicsBuffer<T>
    where T : unmanaged
{
    private readonly BrowserDevice _owner;
    private readonly JSObject _native;
    private bool _mapped;
    private bool _pending;
    private int _registrationCount;
    private bool _disposed;

    internal BrowserBuffer(BrowserDevice owner, BufferDesc<T> desc, BufferLayout<T> layout, ulong size)
    {
        (_owner, Layout, Count, SizeInBytes, Usage, Memory) = (owner, layout, desc.Count, size, desc.Usage, desc.Memory);
        if ((Memory == MemoryPreference.Upload && Usage != BufferUsage.CopySource) ||
            (Memory == MemoryPreference.Readback && Usage != BufferUsage.CopyDestination) ||
            (Memory != MemoryPreference.Automatic && size % 4 != 0) || size > 9007199254740991UL)
        {
            throw new ArgumentException("WebGPU requires staging-only mapped usage and an exactly representable size.", nameof(desc));
        }

        _native = BrowserInterop.CreateBuffer(owner.Handle, size, (int)Usage, (int)Memory);
    }

    public BufferLayout<T> Layout { get; }

    public ulong Count { get; }

    public ulong SizeInBytes { get; }

    public BufferUsage Usage { get; }

    public MemoryPreference Memory { get; }

    public bool IsMapped => _mapped && !_pending && !_disposed;

    internal BrowserDevice Owner => _owner;

    public BufferSlice<T> Slice(ulong offset, ulong count) => new(this, offset, count);

    public void CopyFrom(ReadOnlySpan<T> source) => Slice(0, Count).CopyFrom(source);

    public void CopyTo(Span<T> destination) => Slice(0, Count).CopyTo(destination);

    public void ValidateRange(ulong offset, ulong length)
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
        cancellationToken.ThrowIfCancellationRequested();
        if (Memory == MemoryPreference.Automatic || _mapped || _pending)
        {
            throw new InvalidOperationException("Buffer cannot begin a CPU mapping in its current state.");
        }

        _pending = true;
        return new(MapCoreAsync(cancellationToken));
    }

    public void Unmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_mapped || _pending)
        {
            throw new InvalidOperationException("No completed mapping is available.");
        }

        BrowserInterop.UnmapBuffer(_native);
        _mapped = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_registrationCount != 0)
        {
            throw new InvalidOperationException("Release all argument table registrations before disposing their resource.");
        }

        if (_pending)
        {
            throw new InvalidOperationException("Wait for the mapping request before disposal.");
        }

        BrowserInterop.DestroyBuffer(_native);
        _native.Dispose();
        _mapped = false;
        _disposed = true;
        _owner.ReleaseBuffer();
    }

    void IGraphicsBuffer<T>.CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
    {
        ValidateRange(offset, length);
        RequireMapping(MemoryPreference.Upload);
        if ((ulong)source.Length > length)
        {
            throw new ArgumentException("Source does not fit the buffer range.", nameof(source));
        }

        BrowserInterop.CopyBufferFrom(_native, MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(source), source.Length), checked((int)offset));
    }

    void IGraphicsBuffer<T>.CopyTo(Span<byte> destination, ulong offset, ulong length)
    {
        ValidateRange(offset, length);
        RequireMapping(MemoryPreference.Readback);
        if ((ulong)destination.Length < length)
        {
            throw new ArgumentException("Destination does not fit the complete buffer range.", nameof(destination));
        }

        BrowserInterop.CopyBufferTo(_native, destination[..checked((int)length)], checked((int)offset));
    }

    internal void RetainRegistration()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _registrationCount = checked(_registrationCount + 1);
    }

    internal void ReleaseRegistration() => _registrationCount--;

    private void RequireMapping(MemoryPreference memory)
    {
        if (!_mapped || _pending || Memory != memory)
        {
            throw new InvalidOperationException("CPU copy requires an explicitly mapped buffer of the correct memory direction.");
        }
    }

    private async Task MapCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await BrowserInterop.MapBufferAsync(_native, (int)Memory);
            _mapped = true;
            if (cancellationToken.IsCancellationRequested)
            {
                BrowserInterop.UnmapBuffer(_native);
                _mapped = false;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            _pending = false;
        }
    }
}
