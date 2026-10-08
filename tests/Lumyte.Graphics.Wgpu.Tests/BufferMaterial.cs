namespace Lumyte.Graphics.Tests;

internal readonly record struct BufferMaterial(TextureDescriptorReference Texture, SamplerDescriptorReference Sampler, BufferDescriptorReference<uint> Factor);
