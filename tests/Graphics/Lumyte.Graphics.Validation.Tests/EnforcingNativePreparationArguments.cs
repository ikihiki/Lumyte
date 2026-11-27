using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal readonly struct EnforcingNativePreparationArguments(object? identity) : IShaderArguments
{
    public static string RootParameter => Abi("arguments");

    public static string ShaderTypeName => Abi("PreparedNumber");

    public static int TransferSize => Abi(4);

    public static int ReferenceCount => Abi(0);

    public static IReadOnlyList<ShaderTransferField> TransferFields => Abi<IReadOnlyList<ShaderTransferField>>([new("Value", 0, 4, typeof(uint))]);

    public static bool Poison { get; set; }

    public static int Writes { get; set; }

    public object? NativePreparationIdentity => identity;

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        Abi(0);
        Writes++;
        uint value = 42;
        writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
    }

    private static T Abi<T>(T value) => Poison ? throw new InvalidOperationException("ABI or writer accessed during recording.") : value;
}
