using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly record struct ConsumerLinkArguments(IGpuRef<ConsumerLink> Data, float Float) : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerLinkRoot";

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter
    {
        writer.WriteReference("Data", Data);
        writer.WriteValue("Float", Float);
    }
}
