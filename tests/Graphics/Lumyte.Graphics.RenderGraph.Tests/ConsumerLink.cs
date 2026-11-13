using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal readonly record struct ConsumerLink(IGpuRef<IGraphicsTextureView> Texture) : IShaderData
{
    public static string ShaderTypeName => "ConsumerLink";

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter => writer.WriteReference(0, "Texture", Texture);
}
