using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Pairs nonowning registered image and sampling references.</summary>
/// <param name="Texture">The sampled two-dimensional texture view.</param>
/// <param name="Sampler">The ordinary non-comparison sampler.</param>
public readonly record struct SampledTexture(IGpuRef<IGraphicsTextureView> Texture, IGpuRef<IGraphicsSampler> Sampler);
