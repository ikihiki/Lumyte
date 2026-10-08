namespace Lumyte.Graphics.Tests;

internal readonly record struct BufferMaterial(IGpuRef<IGraphicsTextureView> Texture, IGpuRef<Sampler> Sampler, IGpuRef<uint> Factor);
