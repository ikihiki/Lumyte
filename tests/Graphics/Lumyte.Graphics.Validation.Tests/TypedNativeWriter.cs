using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal struct TypedNativeWriter(List<object?> references) : IShaderValueWriter
{
    public void WriteValue(ReadOnlySpan<byte> value)
    {
    }

    public void WriteReference<T>(IGpuRef<T>? value) => references.Add(value);
}
