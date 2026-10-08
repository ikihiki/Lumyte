using Lumyte.Graphics;

namespace Lumyte.Samples;

// The consumer owns both its logical data and the mapping to its Slang wire fields.
internal sealed class MaterialDataSerializer : IShaderDataSerializer<MaterialData>
{
    internal static MaterialDataSerializer Instance { get; } = new();

    public void Serialize(in MaterialData value, IShaderDataWriter writer)
    {
        writer.Write("baseColor", value.BaseColor);
        writer.WriteTextureReference("textureReference", value.BaseColorTexture);
        writer.WriteSamplerReference("samplerReference", value.Sampler);
        writer.Write("hasTexture", value.BaseColorTexture is not null ? 1u : 0u);
    }
}
