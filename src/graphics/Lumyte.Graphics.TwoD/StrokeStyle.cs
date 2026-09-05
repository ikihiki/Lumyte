namespace Lumyte.Graphics.TwoD;

public sealed class StrokeStyle
{
    private readonly float[] dashes;

    public StrokeStyle(
        float width,
        StrokeJoin join = StrokeJoin.Miter,
        StrokeCap cap = StrokeCap.Butt,
        float miterLimit = 4,
        IEnumerable<float>? dashes = null,
        float dashOffset = 0)
    {
        if (!float.IsFinite(width) || width <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
        if (!Enum.IsDefined(join)) { throw new ArgumentOutOfRangeException(nameof(join)); }
        if (!Enum.IsDefined(cap)) { throw new ArgumentOutOfRangeException(nameof(cap)); }
        if (!float.IsFinite(miterLimit) || miterLimit < 1) { throw new ArgumentOutOfRangeException(nameof(miterLimit)); }
        if (!float.IsFinite(dashOffset)) { throw new ArgumentOutOfRangeException(nameof(dashOffset)); }
        this.dashes = dashes?.ToArray() ?? [];
        if (this.dashes.Any(static value => !float.IsFinite(value) || value <= 0))
        {
            throw new ArgumentException("Dash lengths must be finite and positive.", nameof(dashes));
        }
        if ((this.dashes.Length & 1) != 0)
        {
            this.dashes = [.. this.dashes, .. this.dashes];
        }
        Width = width;
        Join = join;
        Cap = cap;
        MiterLimit = miterLimit;
        DashOffset = dashOffset;
    }

    public float Width { get; }
    public StrokeJoin Join { get; }
    public StrokeCap Cap { get; }
    public float MiterLimit { get; }
    public float DashOffset { get; }
    public ReadOnlyMemory<float> Dashes => dashes;
}
