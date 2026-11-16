using System.Diagnostics.Tracing;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class DrawingProfileScope : IDisposable
{
    private static readonly AsyncLocal<DrawingProfileScope?> _current = new();
    private static long _nextSpan;
    private readonly DrawingProfileScope? _previous;
    private readonly DrawingProfileContext _context;
    private readonly long _span;
    private readonly long _parent;
    private readonly int _stage;
    private readonly bool _stages;
    private readonly bool _counters;
    private int _outcome = 2;

    private DrawingProfileScope(int stage, DrawingProfileContext context, bool root, bool stages, bool counters)
    {
        _previous = _current.Value;
        _context = context;
        _span = Interlocked.Increment(ref _nextSpan);
        _parent = !root && _previous is { _stages: true } ? _previous._span : 0;
        _stage = stage;
        _stages = stages;
        _counters = counters;
        _current.Value = this;
        if (_stages)
        {
            DrawingProfilingEvents.Log.StageBegin(context.RunId, context.FrameId, _span, _parent, stage);
        }
    }

    internal DrawingProfileContext Context => _context;

    internal bool CountersEnabled => _counters;

    public void Dispose()
    {
        if (_stages)
        {
            DrawingProfilingEvents.Log.StageEnd(_context.RunId, _context.FrameId, _span, _parent, _stage, _outcome);
        }

        _current.Value = _previous;
    }

    internal static DrawingProfileScope? Start(int stage, DrawingProfileContext? context = null, bool root = false)
    {
        DrawingProfilingEvents log = DrawingProfilingEvents.Log;
        bool stages = log.IsEnabled(EventLevel.Informational, DrawingProfilingEvents.Keywords.Stages);
        bool counters = log.IsEnabled(EventLevel.Informational, DrawingProfilingEvents.Keywords.Counters);
        return stages || counters ? new(stage, context ?? (root ? DrawingProfileContext.Capture() : _current.Value?._context ?? DrawingProfileContext.Capture()), root, stages, counters) : null;
    }

    internal void ReleaseOperation(int operation)
    {
        if (_stages)
        {
            DrawingProfilingEvents.Log.ReleaseOperation(_context.RunId, _context.FrameId, _span, operation);
        }
    }

    internal void Success() => _outcome = 0;

    internal void Skip() => _outcome = 3;

    internal void Failure(Exception error) => _outcome = error is OperationCanceledException ? 1 : 2;

    internal void Counter(int counterId, long value)
    {
        if (_counters)
        {
            DrawingProfilingEvents.Log.StageCounter(_context.RunId, _context.FrameId, _span, _parent, _stage, counterId, value);
        }
    }

    internal void Snapshot(DrawingRetentionStatistics2D statistics)
    {
        Counter(11, statistics.ActiveFrames);
        Counter(12, statistics.CachedAssets);
        Counter(13, statistics.CachedBytes);
    }
}
