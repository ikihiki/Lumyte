namespace Lumyte.Graphics.Drawing.Gltf;

/// <summary>Defines one validated, CPU-owned indexed triangle mesh.</summary>
/// <param name="Vertices">The object-space vertices.</param>
/// <param name="Indices">The triangle-list indices.</param>
/// <param name="MaterialIndex">The model material index.</param>
public sealed record GltfPrimitive(ModelVertex[] Vertices, uint[] Indices, int MaterialIndex);
