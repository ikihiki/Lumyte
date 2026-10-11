namespace Lumyte.Graphics.Drawing.Gltf;

/// <summary>Groups primitives that can be instantiated by scene nodes.</summary>
/// <param name="Name">The optional asset name.</param>
/// <param name="Primitives">The triangle primitives.</param>
public sealed record GltfMesh(string? Name, IReadOnlyList<GltfPrimitive> Primitives);
