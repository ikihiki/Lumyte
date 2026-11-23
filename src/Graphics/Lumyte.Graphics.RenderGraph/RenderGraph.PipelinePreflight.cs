using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Validates closed dependency declarations without mutating a building graph.</summary>
public sealed partial class RenderGraph
{
    internal void ValidatePassUses(string name, IGraphicDevice device, IReadOnlyList<RenderGraphPassUse> uses)
    {
        ValidateAppend(device, name);
        var detached = new RenderGraphPass(this, name, _ => { });
        foreach (RenderGraphPassUse use in uses)
        {
            use.Apply(this, device, detached);
        }
    }
}
