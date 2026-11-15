using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal readonly struct TypedNumber(uint value = 0, int emittedBytes = 4) : IShaderArguments
{
    public static string ShaderTypeName => "TypedNumber";

    public static int TransferSize => 4;

    public static int ReferenceCount => 0;

    public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Value", 0, 4, typeof(uint))];

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        byte[] data = BitConverter.GetBytes(value);
        writer.WriteValue(data.AsSpan(0, emittedBytes));
    }
}
