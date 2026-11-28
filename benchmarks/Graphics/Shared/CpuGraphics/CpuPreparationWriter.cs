using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal ref struct CpuPreparationWriter(CpuGraphicsDevice owner, Span<byte> destination) : IShaderValueWriter
{
    private readonly List<Action> _registrations = [];
    private CpuWriter _writer = new(owner, destination);

    internal readonly CpuTable? Table => _writer.Table;

    internal readonly Action[] Registrations => _registrations.ToArray();

    public void WriteValue(ReadOnlySpan<byte> value) => _writer.WriteValue(value);

    public void WriteReference<T>(IGpuRef<T>? value)
    {
        _writer.WriteReference(value);
        _registrations.Add(((CpuRef<T>)value!).Validate);
    }

    internal void Complete() => _writer.Complete();
}
