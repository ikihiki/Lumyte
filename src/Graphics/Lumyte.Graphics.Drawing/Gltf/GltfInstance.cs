using System.Numerics;

namespace Lumyte.Graphics.Drawing.Gltf;

/// <summary>Instantiates a mesh in the selected glTF scene.</summary>
/// <param name="Name">The optional node name.</param>
/// <param name="MeshIndex">The model mesh index.</param>
/// <param name="World">The row-vector object-to-world transform.</param>
public sealed record GltfInstance(string? Name, int MeshIndex, Matrix4x4 World);
