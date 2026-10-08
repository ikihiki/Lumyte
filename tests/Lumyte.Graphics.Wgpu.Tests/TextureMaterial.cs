namespace Lumyte.Graphics.Tests;

internal readonly record struct TextureMaterial(IGpuRef<IGraphicsTextureView>[] Textures, IGpuRef<Sampler> Sampler);
