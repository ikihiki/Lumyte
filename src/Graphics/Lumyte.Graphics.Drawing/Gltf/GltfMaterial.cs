namespace Lumyte.Graphics.Drawing.Gltf;

/// <summary>Preserves surface factors and five texture slots for model rendering.</summary>
/// <param name="Name">The optional material name.</param>
/// <param name="Factors">The linear surface factors.</param>
/// <param name="DoubleSided">Whether both faces are rendered.</param>
/// <param name="BaseColorTexture">The sRGB base-color texture index, or null for white.</param>
/// <param name="MetallicRoughnessTexture">The linear metallic-blue/roughness-green texture index, or null for white.</param>
/// <param name="NormalTexture">The linear tangent-space normal texture index, or null for (0.5, 0.5, 1).</param>
/// <param name="OcclusionTexture">The linear occlusion-red texture index, or null for white.</param>
/// <param name="EmissiveTexture">The sRGB emissive texture index, or null for white.</param>
public sealed record GltfMaterial(string? Name, ModelMaterial Factors, bool DoubleSided, int? BaseColorTexture, int? MetallicRoughnessTexture, int? NormalTexture, int? OcclusionTexture, int? EmissiveTexture);
