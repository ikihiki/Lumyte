using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.RenderGraph;

/// <summary>A graph-local destination with the same geometry used by pure 2D renderers.</summary>
public sealed record RenderTargetBinding2D : DrawingTargetInfo2D
{
    /// <summary>Gets the graph-local texture.</summary>
    public required RenderGraphTexture Texture { get; init; }

    /// <summary>Gets the whole single-mip, single-layer range.</summary>
    public TextureSubresourceRange Range { get; init; } = new(0, 1, 0, 1);
}
