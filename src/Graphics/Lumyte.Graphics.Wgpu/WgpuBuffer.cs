using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuBuffer<T> : IGraphicsBuffer<T>
    where T : unmanaged
{
    private readonly WgpuDevice _owner;
    private readonly A.Buffer _native;
    private nint _mappedAddress;
    private bool _mapped;
    private bool _pending;
    private bool _disposed;

    internal WgpuBuffer(WgpuDevice owner, BufferDesc<T> desc, BufferLayout<T> layout, ulong size)
    {
        (_owner, Layout, Count, SizeInBytes, Usage, Memory) = (owner, layout, desc.Count, size, desc.Usage, desc.Memory);
        if ((Memory == MemoryPreference.Upload && Usage != BufferUsage.CopySource) ||
            (Memory == MemoryPreference.Readback && Usage != BufferUsage.CopyDestination) ||
            (Memory != MemoryPreference.Automatic && size % 4 != 0))
        {
            throw new ArgumentException("WebGPU mapping requires a four-byte size and staging-only usage.", nameof(desc));
        }

        A.BufferUsage usage = 0;
        if ((Usage & BufferUsage.CopySource) != 0)
        {
            usage |= A.BufferUsage.CopySrc;
        }

        if ((Usage & BufferUsage.CopyDestination) != 0)
        {
            usage |= A.BufferUsage.CopyDst;
        }

        if ((Usage & (BufferUsage.ShaderRead | BufferUsage.ShaderWrite)) != 0)
        {
            usage |= A.BufferUsage.Storage;
        }

        if ((Usage & BufferUsage.Index) != 0)
        {
            usage |= A.BufferUsage.Index;
        }

        if (Memory == MemoryPreference.Upload)
        {
            usage |= A.BufferUsage.MapWrite;
        }
        else if (Memory == MemoryPreference.Readback)
        {
            usage |= A.BufferUsage.MapRead;
        }

        _native = owner.NativeDevice.CreateBuffer(new A.BufferDescriptor { Size = size, Usage = usage });
    }

    public BufferLayout<T> Layout { get; }

    public ulong Count { get; }

    public ulong SizeInBytes { get; }

    public BufferUsage Usage { get; }

    public MemoryPreference Memory { get; }

    public bool IsMapped
    {
        get
        {
            lock (_owner.BufferGate)
            {
                return _mapped && !_pending && !_disposed;
            }
        }
    }

    public BufferSlice<T> Slice(ulong offset, ulong count) => new(this, offset, count);

    public void CopyFrom(ReadOnlySpan<T> source) => Slice(0, Count).CopyFrom(source);

    public void CopyTo(Span<T> destination) => Slice(0, Count).CopyTo(destination);

    public void ValidateRange(ulong offset, ulong length)
    {
        lock (_owner.BufferGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
        }
    }

    public ValueTask MapAsync(CancellationToken cancellationToken = default)
    {
        lock (_owner.BufferGate)
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
    }

    public void Unmap()
    {
        lock (_owner.BufferGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_mapped || _pending)
            {
                throw new InvalidOperationException("No completed mapping is available.");
            }

            _native.Unmap();
            _mappedAddress = 0;
            _mapped = false;
        }
    }

    public void Dispose()
    {
        lock (_owner.BufferGate)
        {
            if (_disposed)
            {
                return;
            }

            if (_pending)
            {
                throw new InvalidOperationException("Wait for the mapping request before disposal.");
            }

            if (_mapped)
            {
                _native.Unmap();
                _mappedAddress = 0;
                _mapped = false;
            }

            _native.Dispose();
            _disposed = true;
            _owner.ReleaseBuffer();
        }
    }

    void IBufferBackendContract.CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
    {
        lock (_owner.BufferGate)
        {
            ValidateRange(offset, length);
            RequireMapping(MemoryPreference.Upload);
            if ((ulong)source.Length > length)
            {
                throw new ArgumentException("Source does not fit the buffer range.", nameof(source));
            }

            source.CopyTo(GetMappedBytes().Slice(checked((int)offset), source.Length));
        }
    }

    void IBufferBackendContract.CopyTo(Span<byte> destination, ulong offset, ulong length)
    {
        lock (_owner.BufferGate)
        {
            ValidateRange(offset, length);
            RequireMapping(MemoryPreference.Readback);
            if ((ulong)destination.Length < length)
            {
                throw new ArgumentException("Destination does not fit the complete buffer range.", nameof(destination));
            }

            GetMappedBytes().Slice(checked((int)offset), checked((int)length)).CopyTo(destination);
        }
    }

    private void RequireMapping(MemoryPreference memory)
    {
        if (!_mapped || _pending || Memory != memory)
        {
            throw new InvalidOperationException("CPU copy requires an explicitly mapped buffer of the correct memory direction.");
        }
    }

    private async Task MapCoreAsync(CancellationToken cancellationToken)
    {
        A.BufferMapRequest request = default;
        try
        {
            request = _native.BeginMap(Memory == MemoryPreference.Upload ? A.MapMode.Write : A.MapMode.Read, 0, (nuint)SizeInBytes);
            while (true)
            {
                lock (_owner.BufferGate)
                {
                    _owner.NativeDevice.ProcessEvents();
                    if (request.IsComplete)
                    {
                        break;
                    }
                }

                await Task.Delay(1);
            }

            lock (_owner.BufferGate)
            {
                if (request.Status != WGPUMapAsyncStatus.Success)
                {
                    throw new InvalidOperationException($"WebGPU mapping failed: {request.Status}.");
                }

                try
                {
                    CaptureMappedMemory();
                }
                catch
                {
                    _native.Unmap();
                    throw;
                }

                _mapped = true;
                if (cancellationToken.IsCancellationRequested)
                {
                    _native.Unmap();
                    _mappedAddress = 0;
                    _mapped = false;
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
        finally
        {
            request.Dispose();
            lock (_owner.BufferGate)
            {
                _pending = false;
            }
        }
    }

    private unsafe void CaptureMappedMemory()
    {
        ReadOnlySpan<byte> bytes = Memory == MemoryPreference.Upload ? _native.GetMappedRange<byte>(0, (nuint)SizeInBytes) : _native.GetConstMappedRange<byte>(0, (nuint)SizeInBytes);
        _mappedAddress = (nint)Unsafe.AsPointer(ref MemoryMarshal.GetReference(bytes));
    }

    private unsafe Span<byte> GetMappedBytes() => new((void*)_mappedAddress, checked((int)SizeInBytes));
}
