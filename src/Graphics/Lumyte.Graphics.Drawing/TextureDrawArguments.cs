using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines root values for textured drawing.</summary>
/// <param name="Vertices">The vertex range read using SV_VertexID.</param>
/// <param name="Transform">The object-to-clip matrix used with row vectors.</param>
/// <param name="Tint">The linear, straight-alpha color multiplier.</param>
/// <param name="Texture">The sampled two-dimensional texture.</param>
/// <param name="Sampler">The non-comparison texture sampler.</param>
public readonly partial record struct TextureDrawArguments(IGpuRef<DrawingVertex> Vertices, Matrix4x4 Transform, Vector4 Tint, IGpuRef<IGraphicsTextureView> Texture, IGpuRef<IGraphicsSampler> Sampler) : IShaderArguments;
