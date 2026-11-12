using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

// Ordinary application codecs used by both direct encoders and the external graph consumer fixture.
internal readonly record struct ConsumerArguments(float Float, Func<float>? Read = null) : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerInputs";

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter
    {
        writer.WriteValue("Float", Read?.Invoke() ?? Float);
        writer.WriteValue("Int", -2);
        writer.WriteValue("UInt", 3u);
        writer.WriteValue("Vector2", Vector2.One);
        writer.WriteValue("Vector3", Vector3.One);
        writer.WriteValue("Vector4", Vector4.One);
        writer.WriteValue("Matrix", Matrix4x4.Identity);
    }
}
