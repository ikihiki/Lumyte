using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

internal sealed class ManagedDataWriter(int size) : IShaderValueWriter
{
    private int _position;
    private object? _table;

    internal List<byte> Bytes { get; } = [];

    internal List<IShaderReference> References { get; } = [];

    public void WriteValue(ReadOnlySpan<byte> value)
    {
        Validate(value.Length);
        Bytes.AddRange(value.ToArray());
        _position += value.Length;
    }

    public void WriteReference<T>(IGpuRef<T>? value)
    {
        Validate(16);
        if (value is not IShaderReference reference || (_table is not null && !ReferenceEquals(_table, reference.Table)))
        {
            throw new ArgumentException("Native shader-data references must belong to one materialized table domain.");
        }

        _table = reference.Table;
        References.Add(reference);
        _position += 16;
    }

    internal ManagedShaderDataValue Complete(int references)
    {
        if (_position != size || References.Count != references)
        {
            throw new ArgumentException("Native typed upload did not complete its static ABI.");
        }

        return new(Bytes.ToArray(), References.ToArray());
    }

    private void Validate(int count)
    {
        if (count < 0 || count > size - _position)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }
}
