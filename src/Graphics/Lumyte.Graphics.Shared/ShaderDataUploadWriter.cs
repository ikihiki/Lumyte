using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

internal struct ShaderDataUploadWriter(IGraphicsBuffer<byte> storage, ulong offset, int size, Func<ShaderValue, byte[]> encode) : IShaderValueWriter
{
    private List<ShaderValue>? _references;
    private bool _written;

    public void WriteBytes<T>(in T value)
        where T : unmanaged
    {
        if (_written || Unsafe.SizeOf<T>() != size)
        {
            throw new InvalidOperationException("A transfer must write exactly one complete C# wire value.");
        }

        _written = true;
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1));
        storage.CopyBytesFrom(bytes, offset, (ulong)size);
    }

    public void WriteReference<T>(int referenceOffset, IGpuRef<T>? value)
    {
        if (referenceOffset < 0 || referenceOffset > size - 16)
        {
            throw new ArgumentException("Reference slot exceeds the C# wire value.", nameof(referenceOffset));
        }

        if (_references != null)
        {
            foreach (ShaderValue prior in _references)
            {
                if (prior.Offset == referenceOffset)
                {
                    throw new ArgumentException("Reference slots must be unique.", nameof(referenceOffset));
                }
            }
        }

        (_references ??= []).Add(new(typeof(T), value, referenceOffset));
    }

    internal ShaderReferenceMetadata Complete()
    {
        if (!_written)
        {
            throw new InvalidOperationException("The codec did not write a complete C# wire value.");
        }

        foreach (ShaderValue reference in _references ?? (IEnumerable<ShaderValue>)Array.Empty<ShaderValue>())
        {
            byte[] bytes = encode(reference);
            if (bytes.Length != 16)
            {
                throw new ArgumentException("Reference encoding must match the 16-byte ABI.");
            }

            storage.CopyBytesFrom(bytes, checked(offset + (ulong)reference.Offset), 16);
        }

        return new(_references ?? (IEnumerable<ShaderValue>)Array.Empty<ShaderValue>());
    }
}
