using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Drawing2D;

internal sealed record GraphImageSource2D(RenderGraphTexture Texture, TextureDesc Description, EffectPixelRect Bounds);
