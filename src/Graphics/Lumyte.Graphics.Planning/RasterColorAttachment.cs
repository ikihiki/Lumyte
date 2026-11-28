using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>A symbolic color attachment with explicit load and store semantics.</summary>
public sealed record RasterColorAttachment
{
    /// <summary>Gets the attachment view.</summary>
    public required PlanTexture Texture { get; init; }

    /// <summary>Gets the load operation.</summary>
    public AttachmentLoadOp Load { get; init; } = AttachmentLoadOp.Clear;

    /// <summary>Gets the store operation.</summary>
    public AttachmentStoreOp Store { get; init; } = AttachmentStoreOp.Store;

    /// <summary>Gets the clear value.</summary>
    public ClearColor Clear { get; init; }
}
