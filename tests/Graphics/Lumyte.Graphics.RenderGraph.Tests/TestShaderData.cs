using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly struct TestShaderData : IShaderArguments
{
    public static string ShaderTypeName => "TestShaderData";

    public static int TransferSize => System.Runtime.CompilerServices.Unsafe.SizeOf<TestShaderData>();

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct => writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in this, 1)));
}
