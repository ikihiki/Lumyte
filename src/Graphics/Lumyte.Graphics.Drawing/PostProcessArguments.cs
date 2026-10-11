using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines a fullscreen effect pass over a linear scene texture.</summary>
/// <param name="SceneTexture">The sampled source, distinct from the output attachment.</param>
/// <param name="Sampler">The non-comparison clamp-to-edge sampler.</param>
/// <param name="Exposure">The exposure adjustment in stops.</param>
/// <param name="Saturation">The saturation multiplier; zero produces grayscale.</param>
/// <param name="Vignette">The vignette strength in [0, 1].</param>
/// <param name="AspectRatio">The positive output width divided by height.</param>
/// <param name="ToneMap">One applies Reinhard tone mapping; zero preserves the exposed color.</param>
public readonly partial record struct PostProcessArguments(IGpuRef<IGraphicsTextureView> SceneTexture, IGpuRef<IGraphicsSampler> Sampler, float Exposure, float Saturation, float Vignette, float AspectRatio, uint ToneMap) : IShaderArguments;
