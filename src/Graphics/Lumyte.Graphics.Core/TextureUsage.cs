namespace Lumyte.Graphics;

/// <summary>
/// Specifies permitted image operations; combine flags at allocation.
/// </summary>
[Flags]
public enum TextureUsage
{
    /// <summary>
    /// Allows the image to supply GPU copy data.
    /// </summary>
    CopySource = 1,

    /// <summary>
    /// Allows GPU copies to write the image.
    /// </summary>
    CopyDestination = 2,

    /// <summary>
    /// Allows texture views to be sampled by shaders.
    /// </summary>
    Sampled = 4,

    /// <summary>
    /// Allows use as a render pass attachment.
    /// </summary>
    RenderAttachment = 8,
}
