namespace Lumyte.Graphics.RenderGraph;

/// <summary>Describes a render pass using graph-local attachment handles.</summary>
public sealed record RenderGraphRenderPassDesc
{
    /// <summary>Gets an optional diagnostic label.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the ordered logical color attachments.</summary>
    public IReadOnlyList<RenderGraphColorAttachmentDesc> ColorAttachments { get; init; } = [];

    /// <summary>Gets the optional logical depth/stencil attachment.</summary>
    public RenderGraphDepthStencilAttachmentDesc? DepthStencilAttachment { get; init; }
}
