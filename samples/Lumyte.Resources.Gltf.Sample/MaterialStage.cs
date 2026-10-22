using System.Numerics;
using System.Text.Json;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class MaterialStage : IResourceTransform<GltfDocument, CpuMaterial>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    public ResourceAddress GetInput(ResourceAddress output) => new(output.Id);

    public async ValueTask<CpuMaterial> BuildAsync(GltfDocument input, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        JsonElement material = input.Root.GetProperty("materials")[GltfDocument.Select(context.Address, "material")];
        if ((material.TryGetProperty("alphaMode", out JsonElement alpha) && alpha.GetString() != "OPAQUE") ||
            material.TryGetProperty("normalTexture", out _))
        {
            throw new NotSupportedException("This sample renders opaque base-color materials only.");
        }

        JsonElement pbr = material.GetProperty("pbrMetallicRoughness");
        Vector4 color = pbr.TryGetProperty("baseColorFactor", out JsonElement factor) ? new(factor[0].GetSingle(), factor[1].GetSingle(), factor[2].GetSingle(), factor[3].GetSingle()) : Vector4.One;
        JsonElement info = pbr.GetProperty("baseColorTexture");
        if (info.TryGetProperty("texCoord", out JsonElement texCoord) && texCoord.GetInt32() != 0)
        {
            throw new NotSupportedException("Only TEXCOORD_0 is supported.");
        }

        JsonElement texture = input.Root.GetProperty("textures")[info.GetProperty("index").GetInt32()];
        JsonElement image = input.Root.GetProperty("images")[texture.GetProperty("source").GetInt32()];
        ResourceId id = input.Resolve(image.GetProperty("uri").GetString()!);
        var key = new ResourceKey<CpuImage>(id);
        CpuImage decoded = await context.RequireAsync(key, cancellationToken: cancellationToken);
        return new(color, decoded);
    }

    public ValueTask ReleaseAsync(CpuMaterial resource) => ValueTask.CompletedTask;
}
