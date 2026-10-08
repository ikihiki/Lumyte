namespace Lumyte.Graphics;
/// <summary>
/// Describes a render pass with one color attachment.
/// </summary>
public sealed record RenderPassDesc
{
    /// <summary>
    /// Gets the attachment view, which must belong to the encoder device.
    /// </summary>
    public required IGraphicsTextureView Target { get; init; }

    /// <summary>
    /// Gets the initial attachment operation; defaults to clear.
    /// </summary>
    public LoadOp Load { get; init; } = LoadOp.Clear;

    /// <summary>
    /// Gets the final attachment operation; defaults to store.
    /// </summary>
    public StoreOp Store { get; init; } = StoreOp.Store;

    /// <summary>
    /// Gets the color used when the load operation is clear.
    /// </summary>
    public Color4 ClearValue { get; init; }
}
