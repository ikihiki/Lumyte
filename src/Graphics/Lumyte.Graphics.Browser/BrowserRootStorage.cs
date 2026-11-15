using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserRootStorage : IDisposable
{
    private readonly JSObject _native;
    private bool _sealed;
    private bool _disposed;
    private ShaderRootArguments? _arguments;

    internal BrowserRootStorage(BrowserDevice owner, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        // Uniform emulation capacity is private to this WebGPU backend.
        size = Math.Max(16, checked(((size + 15) / 16) * 16));
        if ((ulong)size > owner.Caps.MaxBufferSize)
        {
            throw new NotSupportedException("Root arguments exceed the device buffer size limit.");
        }

        Size = size;
        _native = BrowserInterop.CreateRootBacking(owner.Handle, size);
    }

    internal ShaderRootArguments Arguments => _arguments ?? throw new InvalidOperationException("Root arguments are not initialized.");

    internal BrowserArgumentTable? ImplicitTable { get; private set; }

    internal int Size { get; }

    internal JSObject Native
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
        BrowserInterop.DestroyBuffer(_native);
        _native.Dispose();
    }

    internal void Initialize(ShaderRootArguments arguments, BrowserArgumentTable? table)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_arguments != null)
        {
            throw new InvalidOperationException("Root arguments are already initialized.");
        }

        _arguments = arguments;
        ImplicitTable = table;
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

        // The bridge synchronously reads this view; neither side retains managed input.
        Span<byte> view = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(bytes), bytes.Length);
        BrowserInterop.WriteRootBacking(_native, view, offset);
    }

    internal void Seal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_sealed)
        {
            BrowserInterop.UnmapBuffer(_native);
            _sealed = true;
        }
    }
}
