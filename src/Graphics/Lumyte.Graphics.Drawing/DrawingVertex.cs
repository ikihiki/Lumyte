using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines a position, straight-alpha color and texture coordinate shared by drawing shaders.</summary>
/// <param name="Position">The object-space position.</param>
/// <param name="Color">The linear, straight-alpha vertex color.</param>
/// <param name="TexCoord">The texture coordinate, with (0, 0) at the top left.</param>
public readonly partial record struct DrawingVertex(Vector3 Position, Vector4 Color, Vector2 TexCoord) : IShaderData;
