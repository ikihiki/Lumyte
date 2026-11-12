using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class ClipNormalizationCache2D(DrawingTargetInfo2D target)
{
    private readonly Dictionary<(object Key, Matrix3x2 Transform, bool PixelSpace), NormalizedClip2D> _clips = [];
    private long _bytes;

    internal NormalizedClip2D Get(object key, Matrix3x2 transform, IReadOnlyList<ClipElement2D> elements, bool pixelSpace = false)
    {
        if (_clips.TryGetValue((key, transform, pixelSpace), out NormalizedClip2D? clip))
        {
            return clip;
        }

        clip = NormalizedClip2D.Create(elements, target, pixelSpace);
        long bytes = checked((long)clip.Mask.Length * 16);
        if (_clips.Count < 4096 && _bytes + bytes <= 32 * 1024 * 1024)
        {
            _clips.Add((key, transform, pixelSpace), clip);
            _bytes += bytes;
        }

        return clip;
    }
}
