using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Gltf.Sample;

/// <summary>Stores one expanded glTF vertex for shader pulling.</summary>
/// <param name="Position">The local position.</param>
/// <param name="Normal">The local normal.</param>
/// <param name="TexCoord">The base-color coordinates.</param>
public readonly partial record struct GltfVertex(Vector3 Position, Vector3 Normal, Vector2 TexCoord) : IShaderData;
