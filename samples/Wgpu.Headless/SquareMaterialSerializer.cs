using Lumyte.Graphics;

namespace Lumyte.Samples;

internal sealed class SquareMaterialSerializer : IShaderDataSerializer<SquareMaterial>
{
    internal static SquareMaterialSerializer Instance { get; } = new();

    public void Serialize(in SquareMaterial value, IShaderDataWriter writer)
    {
        writer.Write("rectangle", value.Rectangle);
        writer.WriteSampledTexture2D("textureSelector", value.Texture);
    }
}
