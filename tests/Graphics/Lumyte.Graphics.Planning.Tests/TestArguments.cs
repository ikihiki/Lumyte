using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Provides deterministic managed test instrumentation.</summary>
internal readonly record struct TestArguments(IGpuRef<int>? Buffer = null, byte Value = 42) : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerArguments";

    public static int TransferSize => 17;

    public static int ReferenceCount => 1;

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        writer.WriteValue(new byte[] { Value });
        writer.WriteReference(Buffer);
    }
}
