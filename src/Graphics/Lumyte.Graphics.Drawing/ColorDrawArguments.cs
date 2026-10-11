using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines root values for vertex-color drawing.</summary>
/// <param name="Vertices">The vertex range read using SV_VertexID.</param>
/// <param name="Transform">The object-to-clip matrix used with row vectors.</param>
/// <param name="Tint">The linear, straight-alpha color multiplier.</param>
public readonly partial record struct ColorDrawArguments(IGpuRef<DrawingVertex> Vertices, Matrix4x4 Transform, Vector4 Tint) : IShaderArguments;
