using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly struct TestShaderData : IShaderArguments
{
    public static string ShaderTypeName => "TestShaderData";

    public static int TransferSize => System.Runtime.CompilerServices.Unsafe.SizeOf<TestShaderData>();

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter => ShaderWire.Write(in this, ref writer);
}
