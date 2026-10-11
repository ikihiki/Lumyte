using System.Text.Json;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing.Gltf;

internal sealed partial class GltfReader
{
    private static AddressMode Address(int value) => value switch
    {
        33071 => AddressMode.ClampToEdge,
        33648 => AddressMode.MirrorRepeat,
        10497 => AddressMode.Repeat,
        _ => throw new InvalidDataException("Invalid glTF texture wrapping."),
    };

    private GltfTexture[] ReadTextures(int imageCount)
    {
        JsonElement[] samplers = Array(_root, "samplers");
        JsonElement[] entries = Array(_root, "textures");
        var textures = new GltfTexture[entries.Length];
        for (int i = 0; i < textures.Length; i++)
        {
            int source = entries[i].GetProperty("source").GetInt32();
            CheckIndex(source, imageCount, "image");
            var sampler = new SamplerDesc();
            if (entries[i].TryGetProperty("sampler", out JsonElement samplerId))
            {
                int id = samplerId.GetInt32();
                CheckIndex(id, samplers.Length, "sampler");
                JsonElement entry = samplers[id];
                int min = Int(entry, "minFilter", 9987);
                int mag = Int(entry, "magFilter", 9729);
                if (min is not (9728 or 9729 or 9984 or 9985 or 9986 or 9987) || mag is not (9728 or 9729))
                {
                    throw new InvalidDataException("Invalid glTF texture filter.");
                }

                sampler = new()
                {
                    AddressU = Address(Int(entry, "wrapS", 10497)),
                    AddressV = Address(Int(entry, "wrapT", 10497)),
                    MinFilter = min is 9728 or 9984 or 9986 ? FilterMode.Nearest : FilterMode.Linear,
                    MagFilter = mag == 9728 ? FilterMode.Nearest : FilterMode.Linear,
                    MipmapFilter = min is 9986 or 9987 ? FilterMode.Linear : FilterMode.Nearest,
                    LodMaxClamp = min is 9728 or 9729 ? 0 : 32,
                };
            }

            textures[i] = new(source, sampler);
        }

        return textures;
    }

    private GltfMaterial[] ReadMaterials()
    {
        JsonElement[] entries = Array(_root, "materials");
        var materials = new GltfMaterial[entries.Length + 1];
        for (int i = 0; i < entries.Length; i++)
        {
            JsonElement entry = entries[i];
            JsonElement pbr = entry.TryGetProperty("pbrMetallicRoughness", out JsonElement found) ? found : default;
            float[] baseColor = pbr.ValueKind == JsonValueKind.Undefined ? [1, 1, 1, 1] : Floats(pbr, "baseColorFactor", [1, 1, 1, 1]);
            float[] emissive = Floats(entry, "emissiveFactor", [0, 0, 0]);
            float metallic = pbr.ValueKind == JsonValueKind.Undefined ? 1 : Float(pbr, "metallicFactor", 1);
            float roughness = pbr.ValueKind == JsonValueKind.Undefined ? 1 : Float(pbr, "roughnessFactor", 1);
            uint alpha = (String(entry, "alphaMode") ?? "OPAQUE") switch
            {
                "OPAQUE" => 0,
                "MASK" => 1,
                "BLEND" => 2,
                _ => throw new InvalidDataException("Invalid glTF alpha mode."),
            };
            float cutoff = Float(entry, "alphaCutoff", 0.5f);
            bool unlit = entry.TryGetProperty("extensions", out JsonElement extensions) && extensions.TryGetProperty("KHR_materials_unlit", out _);
            float normalScale = entry.TryGetProperty("normalTexture", out JsonElement normal) ? Float(normal, "scale", 1) : 1;
            float strength = entry.TryGetProperty("occlusionTexture", out JsonElement occlusion) ? Float(occlusion, "strength", 1) : 1;
            if (baseColor.Any(x => x < 0 || x > 1) || emissive.Any(x => x < 0 || x > 1) || metallic < 0 || metallic > 1 || roughness < 0 || roughness > 1 || strength < 0 || strength > 1 || cutoff < 0)
            {
                throw new InvalidDataException("Invalid glTF material factor.");
            }

            var factors = new ModelMaterial(new(baseColor[0], baseColor[1], baseColor[2], baseColor[3]), new(emissive[0], emissive[1], emissive[2]), metallic, roughness, normalScale, strength, cutoff, alpha, unlit ? 1u : 0u);
            materials[i] = new(String(entry, "name"), factors, Bool(entry, "doubleSided"), TextureSlot(pbr, "baseColorTexture"), TextureSlot(pbr, "metallicRoughnessTexture"), TextureSlot(entry, "normalTexture"), TextureSlot(entry, "occlusionTexture"), TextureSlot(entry, "emissiveTexture"));
        }

        materials[^1] = new(null, ModelDrawing.DefaultMaterial, false, null, null, null, null, null);
        return materials;
    }

    private int? TextureSlot(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(property, out JsonElement texture))
        {
            return null;
        }

        if (Int(texture, "texCoord") != 0 || (texture.TryGetProperty("extensions", out JsonElement extensions) && extensions.TryGetProperty("KHR_texture_transform", out _)))
        {
            throw new NotSupportedException("Model shaders currently support only untransformed TEXCOORD_0.");
        }

        int index = texture.GetProperty("index").GetInt32();
        CheckIndex(index, _textures.Length, "texture");
        return index;
    }
}
