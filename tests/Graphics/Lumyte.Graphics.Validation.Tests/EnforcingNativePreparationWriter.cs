using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal ref struct EnforcingNativePreparationWriter(Span<byte> destination) : IShaderValueWriter
{
    private readonly Span<byte> _destination = destination;
    private int _position;

    public void WriteValue(ReadOnlySpan<byte> value)
    {
        value.CopyTo(_destination[_position..]);
        _position += value.Length;
    }

    public void WriteReference<T>(IGpuRef<T>? value) => throw new NotSupportedException();

    internal readonly void Complete()
    {
        if (_position != _destination.Length)
        {
            throw new InvalidOperationException("Incomplete wire.");
        }
    }
}
