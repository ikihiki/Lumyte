using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A logical-space vertex with explicit edge coverage.</summary>
/// <param name="Position">The logical position.</param>
/// <param name="Coverage">The finite coverage between zero and one.</param>
public readonly record struct MeshVertex2D(Vector2 Position, float Coverage);
