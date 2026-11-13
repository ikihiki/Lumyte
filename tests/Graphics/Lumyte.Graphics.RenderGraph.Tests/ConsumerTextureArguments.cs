using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly record struct ConsumerTextureArguments(IGpuRef<IGraphicsTextureView> Input, IGpuRef<IGraphicsTextureView> Backdrop, float Float) : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerTextureInputs";

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter
    {
        writer.WriteReference(0, "Input", Input);
        writer.WriteReference(16, "Backdrop", Backdrop);
        writer.WriteValue(32, "Float", Float);
    }
}
