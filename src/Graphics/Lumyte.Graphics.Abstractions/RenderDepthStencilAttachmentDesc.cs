namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes independent depth and stencil load/store operations on one attachment view.</summary>
public sealed record RenderDepthStencilAttachmentDesc
{
    /// <summary>Gets the single-mip, single-layer depth or depth/stencil view.</summary>
    public required IGraphicsTextureView View { get; init; }

    /// <summary>Gets the depth load operation.</summary>
    public AttachmentLoadOp DepthLoadOp { get; init; } = AttachmentLoadOp.Clear;

    /// <summary>Gets the depth store operation.</summary>
    public AttachmentStoreOp DepthStoreOp { get; init; } = AttachmentStoreOp.Store;

    /// <summary>Gets the finite clear depth in the inclusive zero to one range.</summary>
    public float DepthClearValue { get; init; } = 1;

    /// <summary>Gets the stencil load operation, used only for stencil formats.</summary>
    public AttachmentLoadOp StencilLoadOp { get; init; } = AttachmentLoadOp.Clear;

    /// <summary>Gets the stencil store operation, used only for stencil formats.</summary>
    public AttachmentStoreOp StencilStoreOp { get; init; } = AttachmentStoreOp.Store;

    /// <summary>Gets the eight-bit stencil clear value.</summary>
    public uint StencilClearValue { get; init; }
}
