using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal readonly record struct NumericBitsArguments(IGpuRef<Vector4> Data, IGpuRef<uint> Output) : IShaderArguments
{
    public static string ShaderTypeName => nameof(NumericBitsArguments);

    public static int TransferSize => 32;

    public static int ReferenceCount => 2;

    public static IReadOnlyList<ShaderTransferField> TransferFields =>
        [new(nameof(Data), 0, 16, typeof(ShaderReferenceSlot), true), new(nameof(Output), 16, 16, typeof(ShaderReferenceSlot), true)];

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        writer.WriteReference(Data);
        writer.WriteReference(Output);
    }
}
