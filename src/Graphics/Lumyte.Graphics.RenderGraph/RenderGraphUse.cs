using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

internal readonly record struct RenderGraphUse(RenderGraphResource Resource, TextureState State, BarrierScope Scope);
