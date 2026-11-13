using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed unsafe class Direct3D12RootStorage : IShaderRootStorage
{
    private readonly Direct3D12Device _owner;
    private nint _native;
    private bool _sealed;
    private bool _disposed;

    internal Direct3D12RootStorage(Direct3D12Device owner, int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 16);
        if (size > 65536 || size % 16 != 0)
        {
            throw new NotSupportedException("Root data must be 16-byte aligned and fit the D3D12 constant buffer limit.");
        }

        owner.RequireReady();
        _owner = owner;
        Size = size;
        owner.Check(NativeMethods.RootCreate(owner.Native, checked((uint)size), out _native));
    }

    public int Size { get; }

    internal nint Native
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

    public void Write(ReadOnlySpan<byte> bytes, int offset)
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

        fixed (byte* pointer = bytes)
        {
            _owner.Check(NativeMethods.RootWrite(_native, pointer, checked((uint)bytes.Length), checked((uint)offset)));
        }
    }

    public void Seal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_sealed)
        {
            _owner.Check(NativeMethods.RootSeal(_native));
            _sealed = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeMethods.RootDestroy(_native);
        _native = 0;
    }

    // This bridge belongs to the backend: shared root writers need no native access or IVT.
    private static class NativeMethods
    {
        [DllImport("lumyte_graphics_d3d12", EntryPoint = "root_create", ExactSpelling = true)]
        internal static extern int RootCreate(nint device, uint size, out nint root);

        [DllImport("lumyte_graphics_d3d12", EntryPoint = "root_write", ExactSpelling = true)]
        internal static extern int RootWrite(nint root, byte* bytes, uint size, uint offset);

        [DllImport("lumyte_graphics_d3d12", EntryPoint = "root_seal", ExactSpelling = true)]
        internal static extern int RootSeal(nint root);

        [DllImport("lumyte_graphics_d3d12", EntryPoint = "root_destroy", ExactSpelling = true)]
        internal static extern void RootDestroy(nint root);
    }
}
