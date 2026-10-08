namespace Lumyte.Graphics.Tests;

internal sealed class DelegateShaderDataSerializer<T> : IShaderDataSerializer<T>
{
    private readonly Action<T, IShaderDataWriter> _serialize;

    internal DelegateShaderDataSerializer(Action<T, IShaderDataWriter> serialize)
    {
        _serialize = serialize;
    }

    public void Serialize(in T value, IShaderDataWriter writer) => _serialize(value, writer);
}
