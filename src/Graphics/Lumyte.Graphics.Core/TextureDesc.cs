namespace Lumyte.Graphics;

// Single-sample 2D RGBA8 images.
/// <summary>
/// Describes a single-sample, single-mip, single-layer D2 RGBA8 allocation.
/// </summary>
public sealed record TextureDesc
{
    /// <summary>
    /// Gets the positive image width in texels.
    /// </summary>
    public required uint Width { get; init; }

    /// <summary>
    /// Gets the positive image height in texels.
    /// </summary>
    public required uint Height { get; init; }

    /// <summary>
    /// Gets the operation flags; defaults to a readable render attachment.
    /// </summary>
    public TextureUsage Usage { get; init; } = TextureUsage.RenderAttachment | TextureUsage.CopySource;

    /// <summary>
    /// Gets the image storage format; defaults to RGBA8Unorm.
    /// </summary>
    public TextureFormat Format { get; init; } = TextureFormat.Rgba8Unorm;
}
