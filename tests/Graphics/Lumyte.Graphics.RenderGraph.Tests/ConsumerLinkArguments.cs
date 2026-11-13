using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly record struct ConsumerLinkArguments(IGpuRef<ConsumerLink> Data, float Float) : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerLinkRoot";

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter
    {
        writer.WriteReference(0, "Data", Data);
        writer.WriteValue(16, "Float", Float);
    }
}
