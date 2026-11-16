using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Describes a logical color attachment and its load/store operations.</summary>
public sealed record RenderGraphColorAttachmentDesc
{
    /// <summary>Gets the pass-declared logical texture.</summary>
    public required RenderGraphTexture Texture { get; init; }

    /// <summary>Gets the view description, or the backend default.</summary>
    public TextureViewDesc? ViewDesc { get; init; }

    /// <summary>Gets the pass beginning operation.</summary>
    public required AttachmentLoadOp LoadOp { get; init; }

    /// <summary>Gets the pass ending operation.</summary>
    public required AttachmentStoreOp StoreOp { get; init; }

    /// <summary>Gets the color used by Clear.</summary>
    public ClearColor ClearValue { get; init; }
}
