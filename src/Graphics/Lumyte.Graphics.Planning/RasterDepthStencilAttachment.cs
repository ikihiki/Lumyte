using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>A symbolic depth/stencil attachment with independent aspect operations.</summary>
public sealed record RasterDepthStencilAttachment
{
    /// <summary>Gets the attachment view.</summary>
    public required PlanTexture Texture { get; init; }

    /// <summary>Gets the depth load operation.</summary>
    public AttachmentLoadOp DepthLoad { get; init; } = AttachmentLoadOp.Clear;

    /// <summary>Gets the depth store operation.</summary>
    public AttachmentStoreOp DepthStore { get; init; } = AttachmentStoreOp.Store;

    /// <summary>Gets the clear depth.</summary>
    public float DepthClear { get; init; } = 1;

    /// <summary>Gets the stencil load operation.</summary>
    public AttachmentLoadOp StencilLoad { get; init; } = AttachmentLoadOp.Clear;

    /// <summary>Gets the stencil store operation.</summary>
    public AttachmentStoreOp StencilStore { get; init; } = AttachmentStoreOp.Store;

    /// <summary>Gets the eight-bit clear stencil.</summary>
    public uint StencilClear { get; init; }
}
