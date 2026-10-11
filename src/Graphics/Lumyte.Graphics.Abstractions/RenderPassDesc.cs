namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes a render scope with actual texture views.</summary>
public sealed record RenderPassDesc
{
    /// <summary>Gets an optional diagnostic label.</summary>
    public string? Label { get; init; }

    /// <summary>Gets the ordered color attachments.</summary>
    public IReadOnlyList<RenderColorAttachmentDesc> ColorAttachments { get; init; } = [];

    /// <summary>Gets the optional depth/stencil attachment; at least one attachment is required.</summary>
    public RenderDepthStencilAttachmentDesc? DepthStencilAttachment { get; init; }
}
