using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

internal struct ShaderRootWriter(IShaderRootStorage storage, int wireSize) : IShaderValueWriter
{
    private List<ShaderValue>? _values;
    private bool _written;

    public void WriteBytes<T>(in T value)
        where T : unmanaged
    {
        if (_written)
        {
            throw new InvalidOperationException("A transfer must contain exactly one bulk numeric write.");
        }

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1));
        if (bytes.Length != wireSize)
        {
            throw new InvalidOperationException("The bulk transfer must match the static C# wire size.");
        }

        storage.Write(bytes, 0);
        _written = true;
    }

    public void WriteReference<T>(int offset, IGpuRef<T>? value)
    {
        if (offset < 0 || offset > wireSize - 16)
        {
            throw new ArgumentException("Reference slot exceeds the C# wire value.", nameof(offset));
        }

        foreach (ShaderValue prior in _values ?? [])
        {
            if (prior.Offset == offset)
            {
                throw new ArgumentException("Reference slots must be unique.", nameof(offset));
            }
        }

        (_values ??= []).Add(new(typeof(T), value, offset));
    }

    internal readonly ShaderReferenceMetadata Complete()
    {
        if (!_written)
        {
            throw new InvalidOperationException("A transfer must contain exactly one bulk numeric write.");
        }

        return new(_values ?? (IEnumerable<ShaderValue>)[]);
    }
}
