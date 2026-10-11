using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines a glTF-compatible model vertex in object space.</summary>
/// <param name="Position">The position.</param>
/// <param name="Normal">The unit surface normal.</param>
/// <param name="Tangent">The tangent and bitangent handedness.</param>
/// <param name="TexCoord">The first texture coordinate set.</param>
/// <param name="Color">The linear vertex color.</param>
public readonly partial record struct ModelVertex(Vector3 Position, Vector3 Normal, Vector4 Tangent, Vector2 TexCoord, Vector4 Color) : IShaderData;
