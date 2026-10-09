using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Defines application-owned material values and independent resource references.</summary>
/// <param name="Color">The material multiplier.</param>
/// <param name="Texture">The sampled texture.</param>
/// <param name="Sampler">The independently registered sampler.</param>
public readonly record struct BindingMaterial(Vector4 Color, IGpuRef<IGraphicsTextureView> Texture, IGpuRef<IGraphicsSampler> Sampler) : IShaderData;
