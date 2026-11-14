using Lumyte.Graphics.Abstractions;

// Expected CS0535: handwritten implementations must declare the exact static count.
internal struct MissingReferenceCount : IShaderArguments
{
    public static string ShaderTypeName => "MissingReferenceCount";

    public static int TransferSize => 4;

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct => writer.WriteValue(new byte[4]);
}
