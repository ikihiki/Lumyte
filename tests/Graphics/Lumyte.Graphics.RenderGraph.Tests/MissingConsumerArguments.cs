using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly struct MissingConsumerArguments : IShaderArguments
{
    public static string ShaderTypeName => "OtherInputs";

    public static int TransferSize => 4;

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter
    {
        float value = 1;
        ShaderWire.Write(in value, ref writer);
    }
}
