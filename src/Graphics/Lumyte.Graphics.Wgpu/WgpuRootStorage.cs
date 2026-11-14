using Ahjo.Wgpu.Native;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuRootStorage : IDisposable
{
    private WGPUBufferImpl* _native;
    private void* _mapped;
    private bool _sealed;
    private bool _disposed;
    private ShaderRootArguments? _arguments;

    internal WgpuRootStorage(WgpuDevice owner, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        // Uniform emulation capacity is private to this WebGPU backend.
        size = Math.Max(16, checked(((size + 15) / 16) * 16));
        if ((ulong)size > owner.Caps.MaxBufferSize || (ulong)size > owner.Limits.maxUniformBufferBindingSize)
        {
            throw new NotSupportedException("Root arguments exceed the device uniform size limit.");
        }

        Size = size;
        var descriptor = new WGPUBufferDescriptor { size = (ulong)size, usage = (ulong)A.BufferUsage.Uniform, mappedAtCreation = 1 };
        _native = WGPU.wgpuDeviceCreateBuffer(owner.NativeDevice.Handle, &descriptor);
        if (_native == null)
        {
            throw new InvalidOperationException("WebGPU root allocation failed.");
        }

        try
        {
            _mapped = WGPU.wgpuBufferGetMappedRange(_native, 0, (nuint)size);
            if (_mapped == null)
            {
                throw new InvalidOperationException("WebGPU root mapping failed.");
            }

            new Span<byte>(_mapped, size).Clear();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal ShaderRootArguments Arguments => _arguments ?? throw new InvalidOperationException("Root arguments are not initialized.");

    internal int Size { get; }

    internal WGPUBufferImpl* Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sealed)
            {
                throw new InvalidOperationException("Seal root arguments before binding them.");
            }

            return _native;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_native != null)
        {
            if (_mapped != null)
            {
                WGPU.wgpuBufferUnmap(_native);
                _mapped = null;
            }

            WGPU.wgpuBufferRelease(_native);
            _native = null;
        }
    }

    internal void Initialize(ShaderRootArguments arguments)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_arguments != null)
        {
            throw new InvalidOperationException("Root arguments are already initialized.");
        }

        _arguments = arguments;
    }

    internal void Write(ReadOnlySpan<byte> bytes, int offset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sealed)
        {
            throw new InvalidOperationException("Root arguments are immutable after sealing.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset > Size || bytes.Length > Size - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        bytes.CopyTo(new Span<byte>(_mapped, Size).Slice(offset, bytes.Length));
    }

    internal void Seal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_sealed)
        {
            WGPU.wgpuBufferUnmap(_native);
            _mapped = null;
            _sealed = true;
        }
    }
}
