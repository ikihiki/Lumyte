namespace Lumyte.Graphics.Abstractions;

// Explicit CPU inspection only. Snapshots are not a native upload destination.
internal struct ShaderSnapshotWriter : IShaderValueWriter
{
    internal readonly byte[] Data;
    internal List<ShaderValue>? Values;
    private int _position;
    private bool _written;
    private bool _completed;

    internal ShaderSnapshotWriter(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        Data = new byte[size];
    }

    public void WriteValue(ReadOnlySpan<byte> value)
    {
        ValidateWrite(value.Length);
        value.CopyTo(Data.AsSpan(_position, value.Length));
        _position += value.Length;
        _written = true;
    }

    public void WriteReference<T>(IGpuRef<T>? value)
    {
        ValidateWrite(16);
        (Values ??= []).Add(new(typeof(T), value, _position));
        Data.AsSpan(_position, 16).Clear();
        _position += 16;
        _written = true;
    }

    internal void Complete()
    {
        if (_completed || !_written || _position != Data.Length)
        {
            throw new InvalidOperationException("The snapshot must contain exactly the static C# wire size and be completed once.");
        }

        _completed = true;
    }

    private readonly void ValidateWrite(int size)
    {
        if (_completed)
        {
            throw new InvalidOperationException("The snapshot is already complete.");
        }

        if (size > Data.Length - _position)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "The write exceeds the static C# wire size.");
        }
    }
}
