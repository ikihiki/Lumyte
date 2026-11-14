using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal sealed class Direct3D12RootStorage : IShaderRootStorage
{
    private readonly byte[] _payload;
    private bool _sealed;
    private bool _disposed;

    internal Direct3D12RootStorage(Direct3D12Device owner, int size)
    {
        int words = Direct3D12ShaderTransfer.ValidateRootSize(size);
        owner.RequireReady();
        _payload = new byte[Math.Max(4, checked(words * 4))];
    }

    public int Size => _payload.Length;

    internal ReadOnlySpan<byte> Payload
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_sealed)
            {
                throw new InvalidOperationException("Seal root arguments before binding them.");
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

        bytes.CopyTo(_payload.AsSpan(offset));
    }

    public void Seal()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _sealed = true;
    }

    public void Dispose() => _disposed = true;
}
