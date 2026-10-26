namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly struct TestShaderData : IShaderData
{
    public static string ShaderTypeName => "TestShaderData";

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter
    {
    }
}
