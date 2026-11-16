using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Media;

/// <summary>An immutable prepared image borrowing all GPU resources through the final consuming GPU completion.</summary>
/// <remarks>UI disposal and snapshot replacement never release the view, sampler, allocation or resource owner.</remarks>
public sealed class PreparedImage
{
    /// <summary>Initializes a new instance of the <see cref="PreparedImage"/> class.</summary>
    /// <param name="image">The borrowed sampled image.</param>
    /// <param name="intrinsicSize">Positive finite logical dimensions, independent of texture pixel density.</param>
    /// <param name="source">Positive finite texel rectangle within the sampled view.</param>
    public PreparedImage(Image2D image, Vector2 intrinsicSize, Rect2D source)
    {
        ArgumentNullException.ThrowIfNull(image);
        MediaGeometry.Positive(intrinsicSize.X, nameof(intrinsicSize));
        MediaGeometry.Positive(intrinsicSize.Y, nameof(intrinsicSize));
        MediaGeometry.ViewBox(source, nameof(source));
        (uint width, uint height) = image.View.Texture.GetMipSize(image.View.Info.BaseMipLevel);
        if (source.X < 0 || source.Y < 0 || (double)source.X + source.Width > width || (double)source.Y + source.Height > height)
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        Image = image;
        IntrinsicSize = intrinsicSize;
        Source = source;
    }

    /// <summary>Gets the borrowed image. Retain its owner through every consuming frame's GPU completion.</summary>
    public Image2D Image { get; }

    /// <summary>Gets intrinsic logical dimensions.</summary>
    public Vector2 IntrinsicSize { get; }

    /// <summary>Gets the texel rectangle.</summary>
    public Rect2D Source { get; }
}
