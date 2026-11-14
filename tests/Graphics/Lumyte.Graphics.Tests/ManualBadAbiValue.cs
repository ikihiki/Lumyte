using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

// A manual producer using exactly the generated value's ABI and actual backend writer.
internal struct ManualBadAbiValue(NativeAbiValue value, int mode = 0) : IShaderArguments
{
    public static string ShaderTypeName => "NativeAbiValue";

    public static int TransferSize => 88;

    public static int ReferenceCount => 5;

    public static IReadOnlyList<ShaderTransferField> TransferFields => Fields<NativeAbiValue>();

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        if (mode == 0)
        {
            Forward(in value, ref writer);
            return;
        }

        writer.WriteValue(new byte[4]);
        if (mode == 2)
        {
            writer.WriteValue(new byte[85]);
        }
    }

    private static IReadOnlyList<ShaderTransferField> Fields<T>()
        where T : struct, IShaderArguments => T.TransferFields;

    private static void Forward<T, TWriter>(in T value, ref TWriter writer)
        where T : struct, IShaderArguments
        where TWriter : IShaderValueWriter, allows ref struct => value.Write(ref writer);
}
