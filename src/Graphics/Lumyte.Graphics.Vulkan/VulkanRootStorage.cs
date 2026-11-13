using Lumyte.Graphics.Abstractions;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal sealed unsafe class VulkanRootStorage : IShaderRootStorage
{
    private readonly VulkanShaderBacking _backing;
    private void* _mapped;
    private bool _sealed;
    private bool _disposed;

    internal VulkanRootStorage(VulkanDevice owner, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        if ((ulong)size > owner.Caps.MaxBufferSize || (ulong)size > owner.MaxRootUniformSize)
        {
            throw new NotSupportedException("Root arguments exceed the device uniform size limit.");
        }

        Size = size;
        _backing = new(owner, size);
        try
        {
            _mapped = _backing.Map();
            new Span<byte>(_mapped, Size).Clear();
        }
        catch
        {
            _backing.Dispose();
            throw;
        }
    }

    public int Size { get; }

    internal V.Buffer Native => !_disposed && _sealed ? _backing.Native : throw new InvalidOperationException("Seal live root storage before binding.");

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

        bytes.CopyTo(new Span<byte>(_mapped, Size).Slice(offset, bytes.Length));
    }

    public void Seal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_sealed)
        {
            _backing.Unmap();
            _mapped = null;
            _sealed = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_mapped != null)
        {
            _backing.Unmap();
            _mapped = null;
        }

        _backing.Dispose();
        _disposed = true;
    }
}
