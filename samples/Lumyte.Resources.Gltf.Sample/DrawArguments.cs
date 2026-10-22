using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Gltf.Sample;

/// <summary>Supplies geometry, camera and independently resolved material bindings.</summary>
/// <param name="Vertices">The expanded triangle vertices.</param>
/// <param name="Transform">The local to clip transform.</param>
/// <param name="Color">The base-color factor.</param>
/// <param name="Texture">The external base-color texture.</param>
/// <param name="Sampler">The texture sampler.</param>
public readonly partial record struct DrawArguments(IGpuRef<GltfVertex> Vertices, Matrix4x4 Transform, Vector4 Color, IGpuRef<IGraphicsTextureView> Texture, IGpuRef<IGraphicsSampler> Sampler) : IShaderArguments;
