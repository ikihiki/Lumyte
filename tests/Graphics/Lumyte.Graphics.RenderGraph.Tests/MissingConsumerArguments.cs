using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly struct MissingConsumerArguments : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerInputs";

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter => writer.WriteValue("Float", 1f);
}
