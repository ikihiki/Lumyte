using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>A graph-local, single-mip/layer, linear premultiplied Rgba16Float image.</summary>
/// <param name="Texture">The graph-local texture handle.</param>
/// <param name="Bounds">The positive-sized rectangle whose top-left maps to texture coordinate zero.</param>
/// <param name="ColorSemantic">The linear color interpretation.</param>
public readonly record struct EffectImage(RenderGraphTexture Texture, EffectPixelRect Bounds, EffectColorSemantic ColorSemantic);
