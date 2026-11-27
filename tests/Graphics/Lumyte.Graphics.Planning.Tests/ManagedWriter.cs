using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Provides deterministic managed test instrumentation.</summary>
internal readonly struct ManagedWriter(List<byte> bytes, List<object> references) : IShaderValueWriter
{
    public void WriteValue(ReadOnlySpan<byte> value) => bytes.AddRange(value.ToArray());

    public void WriteReference<T>(IGpuRef<T>? value)
    {
        if (value is not null)
        {
            references.Add(value);
        }
    }
}
