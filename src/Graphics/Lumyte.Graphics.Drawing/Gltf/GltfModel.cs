namespace Lumyte.Graphics.Drawing.Gltf;

/// <summary>Contains static meshes, materials and instances in a selected scene.</summary>
/// <param name="Meshes">The CPU meshes.</param>
/// <param name="Materials">The materials, including a default material.</param>
/// <param name="Textures">The texture/image/sampler associations.</param>
/// <param name="Images">The encoded image data.</param>
/// <param name="Instances">The selected scene instances.</param>
public sealed record GltfModel(IReadOnlyList<GltfMesh> Meshes, IReadOnlyList<GltfMaterial> Materials, IReadOnlyList<GltfTexture> Textures, IReadOnlyList<GltfImage> Images, IReadOnlyList<GltfInstance> Instances);
