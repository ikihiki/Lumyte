using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanRootStorage : IShaderRootStorage
{
    private readonly byte[] _payload;
    private bool _sealed;
    private bool _disposed;

    internal VulkanRootStorage(VulkanDevice owner, int size)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        Size = Math.Max(4, checked((size + 3) & ~3));
        _payload = new byte[Size];
    }

    public int Size { get; }

    internal ReadOnlySpan<byte> Data
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sealed)
            {
                throw new InvalidOperationException("Seal root storage before pushing command payloads.");
            }

            return _payload;
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

        bytes.CopyTo(_payload.AsSpan(offset, bytes.Length));
    }

    public void Seal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _sealed = true;
    }

    public void Dispose() => _disposed = true;
}
