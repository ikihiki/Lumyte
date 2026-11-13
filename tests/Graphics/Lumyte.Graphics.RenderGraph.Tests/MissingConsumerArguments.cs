using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly struct MissingConsumerArguments : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerInputs";

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter => writer.WriteValue(0, "Float", 1f);
}
