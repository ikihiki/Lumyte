using System.Numerics;

namespace Lumyte.Graphics.Tests;

internal readonly record struct CustomMaterial(float Opacity, Vector4 Tint, IGpuRef<IGraphicsTextureView> Texture, IGpuRef<Sampler> Sampler);
