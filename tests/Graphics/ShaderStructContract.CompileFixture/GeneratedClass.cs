using Lumyte.Graphics.Abstractions;

// Expected: LUMG001, even though this class supplies a handwritten implementation.
internal partial class GeneratedClass : IShaderArguments
{
    public static string ShaderTypeName => "Rejected";
    public static int TransferSize => 4;
    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct { }
}
