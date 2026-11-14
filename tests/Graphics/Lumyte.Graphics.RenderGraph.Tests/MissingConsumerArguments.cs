using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly struct MissingConsumerArguments : IShaderArguments
{
    public static string ShaderTypeName => "OtherInputs";

    public static int TransferSize => 4;

    public static int ReferenceCount => 0;

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        float value = 1;
        writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
    }
}
