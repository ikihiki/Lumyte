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
        writer.WriteValue(0, "Float", Read?.Invoke() ?? Float);
        writer.WriteValue(4, "Int", -2);
        writer.WriteValue(8, "UInt", 3u);
        writer.WriteValue(16, "Vector2", Vector2.One);
        writer.WriteValue(32, "Vector3", Vector3.One);
        writer.WriteValue(48, "Vector4", Vector4.One);
        writer.WriteValue(64, "Matrix", Matrix4x4.Identity);
    }
}
