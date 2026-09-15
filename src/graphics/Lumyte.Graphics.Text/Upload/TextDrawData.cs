using System.Numerics;

using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

public sealed record TextLineMetricsData(float Baseline, float Ascent, float Descent, Rect AdvanceBounds, Rect InkBounds);
public sealed record PositionedGlyphData(GlyphUploadData Glyph, Matrix3x2 Transform, Vector2 Advance, int Utf16Cluster);
public sealed record GlyphOutline(PathGeometry Path, FillRule FillRule = FillRule.NonZero);

public sealed class TextGlyphRunData
{
    public TextGlyphRunData(IEnumerable<PositionedGlyphData> glyphs)
    { ArgumentNullException.ThrowIfNull(glyphs); Glyphs = Array.AsReadOnly(glyphs.ToArray()); }
    public IReadOnlyList<PositionedGlyphData> Glyphs { get; }
}

public sealed class TextDrawData : IGpuUploadData
{
    public TextDrawData(GpuUploadDataKey key, Vector2 size, IEnumerable<TextLineMetricsData> lines, IEnumerable<TextGlyphRunData> glyphRuns)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(glyphRuns);
        Key = key;
        Size = size;
        Lines = Array.AsReadOnly(lines.ToArray());
        GlyphRuns = Array.AsReadOnly(glyphRuns.ToArray());
        var uploads = new HashSet<IGpuUploadData>(ReferenceEqualityComparer.Instance) { this };
        foreach (var glyph in GlyphRuns.SelectMany(run => run.Glyphs))
        { foreach (var upload in glyph.Glyph.Uploads) { uploads.Add(upload); } }
        Uploads = Array.AsReadOnly(uploads.ToArray());
    }
    public GpuUploadDataKey Key { get; }
    public Vector2 Size { get; }
    public IReadOnlyList<TextLineMetricsData> Lines { get; }
    public IReadOnlyList<TextGlyphRunData> GlyphRuns { get; }
    public IReadOnlyList<IGpuUploadData> Uploads { get; }
}

public sealed class GlyphUploadData : IGpuUploadData
{
    public GlyphUploadData(GpuUploadDataKey key, Rect bounds, GlyphOutline? outline = null, GlyphPaintNode? colorPaint = null)
    {
        Key = key;
        Bounds = bounds;
        Outline = outline;
        ColorPaint = colorPaint;
        var uploads = new HashSet<IGpuUploadData>(ReferenceEqualityComparer.Instance) { this };
        if (colorPaint is not null)
        { foreach (var image in colorPaint.Images) { uploads.Add(image); } }
        Uploads = Array.AsReadOnly(uploads.ToArray());
    }
    public GpuUploadDataKey Key { get; }
    public Rect Bounds { get; }
    public GlyphOutline? Outline { get; }
    public GlyphPaintNode? ColorPaint { get; }
    public IReadOnlyList<IGpuUploadData> Uploads { get; }
}

public enum DistanceFieldKind { Coverage, Sdf, Msdf }
public sealed class DistanceFieldUploadData : IGpuUploadData
{
    public DistanceFieldUploadData(GpuUploadDataKey key, GpuImageUploadData image, DistanceFieldKind kind, Vector2 sourceSize, float distanceRange)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!Enum.IsDefined(kind))
        { throw new ArgumentOutOfRangeException(nameof(kind)); }
        if (!float.IsFinite(sourceSize.X) || !float.IsFinite(sourceSize.Y) || sourceSize.X <= 0 || sourceSize.Y <= 0)
        { throw new ArgumentOutOfRangeException(nameof(sourceSize)); }
        if (!float.IsFinite(distanceRange) || (kind == DistanceFieldKind.Coverage ? distanceRange != 0 : distanceRange <= 0))
        { throw new ArgumentOutOfRangeException(nameof(distanceRange)); }
        if (image.Encoding != GpuImageColorEncoding.Data && image.Encoding != GpuImageColorEncoding.Linear)
        { throw new ArgumentException("Distance fields require linear sample values.", nameof(image)); }
        Key = key;
        Image = image;
        Kind = kind;
        SourceSize = sourceSize;
        DistanceRange = distanceRange;
    }
    public GpuUploadDataKey Key { get; }
    public GpuImageUploadData Image { get; }
    public DistanceFieldKind Kind { get; }
    public Vector2 SourceSize { get; }
    public float DistanceRange { get; }
}
