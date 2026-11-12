using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

internal readonly record struct RenderGraphUse(RenderGraphResource Resource, TextureState State, BarrierScope Scope, bool Discard = false, IReadOnlyList<GraphShaderBinding>? Bindings = null);
