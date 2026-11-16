using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class ClipNormalizationCache2D(DrawingTargetInfo2D target)
{
    private Dictionary<(object Key, Matrix3x2 Transform, bool PixelSpace), NormalizedClip2D> _clips = [];
    private Dictionary<int, List<(ClipElement2D[] Elements, bool PixelSpace, NormalizedClip2D Clip)>> _equivalent = [];
    private long _bytes;

    // Conservative geometry/container charge; 32 MiB is an estimated payload
    // budget, not a CLR heap-size assertion. Identity aliases are charged too.
    private long _geometryBytes;

    internal void Clear()
    {
        _clips = [];
        _equivalent = [];
        _bytes = 0;
        _geometryBytes = 0;
    }

    internal NormalizedClip2D Get(object key, Matrix3x2 transform, IReadOnlyList<ClipElement2D> elements, bool pixelSpace = false)
    {
        if (_clips.TryGetValue((key, transform, pixelSpace), out NormalizedClip2D? clip))
        {
            return clip;
        }

        // Captured element transforms, not the caller's current transform, define
        // geometry. Hashes only select a bucket: equality checks every segment.
        if (elements.Count > 4096)
        {
            return NormalizedClip2D.Create(elements, target, pixelSpace);
        }

        int segmentCount = 0;
        foreach (ClipElement2D element in elements)
        {
            if (element.Path.Segments.Count > 65536 - segmentCount)
            {
                // Skip structural lookup rather than scan unbounded source input.
                return NormalizedClip2D.Create(elements, target, pixelSpace);
            }

            segmentCount += element.Path.Segments.Count;
        }

        long geometryBytes = 256;
        HashCode hash = default;
        hash.Add(pixelSpace);
        foreach (ClipElement2D element in elements)
        {
            hash.Add(element.Transform);
            hash.Add(element.Rule);
            hash.Add(element.Path.Segments.Count);
            geometryBytes = checked(geometryBytes + ((long)element.Path.Segments.Count * 32));
            foreach (PathSegment2D segment in element.Path.Segments)
            {
                hash.Add(segment);
            }
        }

        int code = hash.ToHashCode();
        if (_equivalent.TryGetValue(code, out List<(ClipElement2D[] Elements, bool PixelSpace, NormalizedClip2D Clip)>? entries))
        {
            foreach ((ClipElement2D[] Elements, bool PixelSpace, NormalizedClip2D Clip) entry in entries)
            {
                if (entry.PixelSpace == pixelSpace && Equal(entry.Elements, elements))
                {
                    clip = entry.Clip;
                    long aliasBytes = checked(geometryBytes + ((long)elements.Count * 256));
                    if (_clips.Count < 4096 && _bytes + _geometryBytes + aliasBytes <= 32 * 1024 * 1024)
                    {
                        _clips.Add((key, transform, pixelSpace), clip);
                        _geometryBytes += aliasBytes;
                    }

                    return clip;
                }
            }
        }

        clip = NormalizedClip2D.Create(elements, target, pixelSpace);
        long bytes = checked((long)clip.Mask.Length * 16);
        geometryBytes = checked(geometryBytes + ((long)elements.Count * 256));
        if (_clips.Count < 4096 && _bytes + bytes + _geometryBytes + geometryBytes <= 32 * 1024 * 1024)
        {
            _clips.Add((key, transform, pixelSpace), clip);
            if (entries is null)
            {
                entries = [];
                _equivalent.Add(code, entries);
            }

            entries.Add((elements.ToArray(), pixelSpace, clip));
            _bytes += bytes;
            _geometryBytes += geometryBytes;
        }

        return clip;
    }

    private static bool Equal(ClipElement2D[] left, IReadOnlyList<ClipElement2D> right)
    {
        if (left.Length != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            ClipElement2D a = left[i], b = right[i];
            if (a.Transform != b.Transform || a.Rule != b.Rule
                || a.Path.Segments.Count != b.Path.Segments.Count)
            {
                return false;
            }

            if (!ReferenceEquals(a.Path, b.Path))
            {
                for (int j = 0; j < a.Path.Segments.Count; j++)
                {
                    if (a.Path.Segments[j] != b.Path.Segments[j])
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
