namespace Lumyte.Graphics.ImageEffects;

/// <summary>Composes two images after translating the source.</summary>
/// <param name="Source">The source operation.</param>
/// <param name="Backdrop">The backdrop operation.</param>
/// <param name="Mode">The composition operator.</param>
/// <param name="SourceOffsetX">The horizontal integer offset.</param>
/// <param name="SourceOffsetY">The vertical integer offset.</param>
public sealed record CompositeEffectNode(ImageEffectNode Source, ImageEffectNode Backdrop, EffectCompositeMode Mode, int SourceOffsetX, int SourceOffsetY) : ImageEffectNode;
