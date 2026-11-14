using Lumyte.Graphics.Abstractions;

// Expected: LUMG001; allows-ref-struct writers do not make arguments ref structs legal.
internal ref partial struct RefStruct : IShaderArguments
{
    public static string ShaderTypeName => "Rejected";
    public static int TransferSize => 4;
    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct { }
}
