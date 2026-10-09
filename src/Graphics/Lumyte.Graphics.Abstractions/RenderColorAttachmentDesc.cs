namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes a color rendering destination and its load/store operations.</summary>
public sealed record RenderColorAttachmentDesc
{
    /// <summary>Gets the exact single-mip single-layer view.</summary>
    public required IGraphicsTextureView View { get; init; }

    /// <summary>Gets the pass beginning operation.</summary>
    public required AttachmentLoadOp LoadOp { get; init; }

    /// <summary>Gets the pass ending operation.</summary>
    public required AttachmentStoreOp StoreOp { get; init; }

    /// <summary>Gets the color used by Clear.</summary>
    public ClearColor ClearValue { get; init; }
}
