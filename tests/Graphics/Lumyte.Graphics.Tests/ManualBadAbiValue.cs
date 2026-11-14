using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

// A manual producer using exactly the generated value's ABI and actual backend writer.
internal sealed class ManualBadAbiValue(NativeAbiValue value, int mode = 0) : IShaderArguments
{
    public static string ShaderTypeName => "NativeAbiValue";

    public static int TransferSize => 88;

    public IReadOnlyList<ShaderTransferField> GetTransferFields() => ((IShaderArguments)value).GetTransferFields();

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        if (mode == 0)
        {
            ((IShaderArguments)value).Write(ref writer);
            return;
        }

        writer.WriteValue(new byte[4]);
        if (mode == 2)
        {
            writer.WriteValue(new byte[85]);
        }
    }
}
