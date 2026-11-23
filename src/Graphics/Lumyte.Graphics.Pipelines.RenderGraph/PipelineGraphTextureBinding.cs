using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Pipelines.RenderGraph;

/// <summary>Binds an opaque texture slot to an already imported actual native allocation.</summary>
/// <param name="Slot">The exact manifest-local slot.</param>
/// <param name="Resource">The already imported logical texture.</param>
/// <param name="Allocation">The same actual native allocation.</param>
/// <param name="State">The single state used by every stage and at exit.</param>
/// <param name="Scope">The complete actual access union.</param>
public sealed record PipelineGraphTextureBinding(PipelineResourceSlot Slot, RenderGraphTexture Resource, IGraphicsTexture Allocation, TextureState State, BarrierScope Scope);
