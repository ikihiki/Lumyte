using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.Text;

internal sealed class HarfBuzzOutlineSink
{
    private Exception? failure;
    private int drawableSegmentCount;

    internal PathBuilder Builder { get; } = new();
    internal Exception? Failure => Volatile.Read(ref failure);

    internal void AddDrawableSegment() => drawableSegmentCount++;

    internal void RecordFailure(Exception exception)
        => Interlocked.CompareExchange(ref failure, exception, null);

    internal bool TryBuild(out PathGeometry? path)
    {
        path = drawableSegmentCount == 0 ? null : Builder.Build();
        return path is not null;
    }
}
