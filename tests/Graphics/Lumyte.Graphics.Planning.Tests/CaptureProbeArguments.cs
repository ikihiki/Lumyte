using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

internal readonly record struct CaptureProbeArguments(List<string> Events) : IShaderArguments
{
    public static string ShaderTypeName => "CaptureProbe";

    public static int TransferSize => 1;

    public static int ReferenceCount => 0;

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        Events.Add("capture");
        writer.WriteValue([7]);
    }
}
