using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;

namespace Lumyte.Graphics.RenderGraph;

[EventSource(Name = "Lumyte-RenderGraph-Profiling")]
internal sealed class RenderGraphProfiling : EventSource
{
    [ThreadStatic]
    private static ProfileScope? _current;
    private static long _nextSpan;

    private RenderGraphProfiling()
    {
    }

    internal static RenderGraphProfiling Log { get; } = new();

    internal static Guid CurrentRunId => _current?.ProfileRunId ?? default;

    internal static long CurrentFrameId => _current?.FrameId ?? 0;

    internal bool HasMeasurements => IsEnabled(EventLevel.Informational, Keywords.Stages | Keywords.Counters | Keywords.Passes | Keywords.Control);

    [Event(1, Level = EventLevel.Informational, Keywords = Keywords.Stages)]
    public void StageBegin(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage)
        => WriteStage(1, profileRunId, frameId, spanId, parentSpanId, stage, 0, 0, 5);

    [Event(2, Level = EventLevel.Informational, Keywords = Keywords.Stages)]
    public void StageEnd(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int outcome)
        => WriteStage(2, profileRunId, frameId, spanId, parentSpanId, stage, outcome, 0, 6);

    [Event(3, Level = EventLevel.Informational, Keywords = Keywords.Counters)]
    public void StageCounter(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int counterId, long value)
        => WriteStage(3, profileRunId, frameId, spanId, parentSpanId, stage, counterId, value, 7);

    [Event(4, Level = EventLevel.Informational, Keywords = Keywords.Control)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "EventData contains only fixed-width Guid, long and int values; no object graph or reflected properties are serialized.")]
    public unsafe void GraphRelation(Guid graphId, Guid graphRunId, Guid profileRunId, long frameId, long rootSpanId, int operation, int frameMatch)
    {
        EventData* data = stackalloc EventData[7];
        data[0] = Data(&graphId, 16);
        data[1] = Data(&graphRunId, 16);
        data[2] = Data(&profileRunId, 16);
        data[3] = Data(&frameId, 8);
        data[4] = Data(&rootSpanId, 8);
        data[5] = Data(&operation, 4);
        data[6] = Data(&frameMatch, 4);
        WriteEventCore(4, 7, data);
    }

    [Event(5, Level = EventLevel.Informational, Keywords = Keywords.Passes)]
    public void PassBegin(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int executionIndex)
        => WriteStage(5, profileRunId, frameId, spanId, parentSpanId, stage, executionIndex, 0, 6);

    [Event(6, Level = EventLevel.Informational, Keywords = Keywords.Passes)]
    public void PassEnd(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int executionIndex, int outcome)
        => WriteStage(6, profileRunId, frameId, spanId, parentSpanId, stage, executionIndex, outcome, 8);

    [Event(7, Level = EventLevel.Informational, Keywords = Keywords.Control)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "EventData contains only fixed-width Guid, long and int values; no object graph or reflected properties are serialized.")]
    public unsafe void GraphOperationResult(Guid graphRunId, long rootSpanId, int operation, int outcome)
    {
        if (!IsEnabled(EventLevel.Informational, Keywords.Control))
        {
            return;
        }

        EventData* data = stackalloc EventData[4];
        data[0] = Data(&graphRunId, 16);
        data[1] = Data(&rootSpanId, 8);
        data[2] = Data(&operation, 4);
        data[3] = Data(&outcome, 4);
        WriteEventCore(7, 4, data);
    }

    [NonEvent]
    internal static (Guid RunId, long FrameId) ReadContext()
    {
        for (Activity? activity = Activity.Current; activity is not null; activity = activity.Parent)
        {
            if (activity.GetCustomProperty("Lumyte.Profiling.RunId") is Guid runId && activity.GetCustomProperty("Lumyte.Profiling.FrameId") is long frameId)
            {
                return (runId, frameId);
            }
        }

        return default;
    }

    [NonEvent]
    internal static void Count(int id, long value = 1)
    {
        for (ProfileScope? scope = _current; scope is not null; scope = scope.Parent)
        {
            if (scope.Stage is 1 or 5 or 7)
            {
                scope.Add(id, value);
                return;
            }
        }
    }

    [NonEvent]
    internal ProfileScope? Begin(int stage, RenderGraphRun run, Guid profileRunId, long frameId, int operation = 0, int passIndex = -1)
    {
        EventKeywords keyword = stage == 8 ? Keywords.Passes : Keywords.Stages;
        bool events = IsEnabled(EventLevel.Informational, keyword);
        if (stage == 8 && !events)
        {
            return null;
        }

        bool passParent = operation != 0 && IsEnabled(EventLevel.Informational, Keywords.Passes);
        if (!events && !passParent && !IsEnabled(EventLevel.Informational, Keywords.Counters) && (operation == 0 || !IsEnabled(EventLevel.Informational, Keywords.Control)))
        {
            return null;
        }

        var scope = new ProfileScope(this, _current, stage, profileRunId, frameId, passIndex, events);
        _current = scope;
        if (operation != 0 && IsEnabled(EventLevel.Informational, Keywords.Control))
        {
            int match = run.FrameId == 0 || frameId == 0 ? 0 : run.FrameId == frameId ? 1 : 2;
            GraphRelation(run.GraphId, run.RunId, profileRunId, frameId, scope.SpanId, operation, match);
        }

        scope.Start();
        return scope;
    }

    [NonEvent]
    internal ProfileScope? BeginChild(int stage, int passIndex = -1)
    {
        ProfileScope? parent = _current;
        return parent is null ? null : Begin(stage, default, parent.ProfileRunId, parent.FrameId, passIndex: passIndex);
    }

    private static unsafe EventData Data(void* pointer, int size) => new() { DataPointer = (IntPtr)pointer, Size = size };

    [NonEvent]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "EventData contains only fixed-width Guid, long and int values; no object graph or reflected properties are serialized.")]
    private unsafe void WriteStage(int id, Guid run, long frame, long span, long parent, int stage, int tail, long value, int count)
    {
        EventData* data = stackalloc EventData[7];
        data[0] = Data(&run, 16);
        data[1] = Data(&frame, 8);
        data[2] = Data(&span, 8);
        data[3] = Data(&parent, 8);
        data[4] = Data(&stage, 4);
        data[5] = Data(&tail, 4);
        int outcome = (int)value;
        data[6] = count == 8 ? Data(&outcome, 4) : Data(&value, 8);
        WriteEventCore(id, count == 8 ? 7 : count, data);
    }

    public static class Keywords
    {
        public const EventKeywords Control = (EventKeywords)1;
        public const EventKeywords Stages = (EventKeywords)2;
        public const EventKeywords Counters = (EventKeywords)4;
        public const EventKeywords Passes = (EventKeywords)16;
    }

    internal sealed class ProfileScope : IDisposable
    {
        private readonly RenderGraphProfiling _source;
        private readonly bool _events;
        private readonly int _passIndex;
        private readonly long[]? _counts;
        private uint _present;
        private int _outcome = 2;
        private bool _disposed;

        internal ProfileScope(RenderGraphProfiling source, ProfileScope? parent, int stage, Guid runId, long frameId, int passIndex, bool events)
        {
            _source = source;
            _counts = stage is 1 or 5 or 7 && source.IsEnabled(EventLevel.Informational, Keywords.Counters) ? new long[13] : null;
            Parent = parent;
            Stage = stage;
            ProfileRunId = runId;
            FrameId = frameId;
            SpanId = Interlocked.Increment(ref _nextSpan);
            ParentSpanId = parent is not null && parent.ProfileRunId == runId && parent.FrameId == frameId ? parent.SpanId : 0;
            _passIndex = passIndex;
            _events = events;
        }

        internal ProfileScope? Parent { get; }

        internal Guid ProfileRunId { get; }

        internal long FrameId { get; }

        internal long SpanId { get; }

        internal long ParentSpanId { get; }

        internal int Stage { get; }

        internal int Outcome => _outcome;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _current = Parent;
            if (_counts is not null && _source.IsEnabled(EventLevel.Informational, Keywords.Counters))
            {
                for (int id = 1; id < _counts.Length; id++)
                {
                    if ((_present & (1U << id)) != 0)
                    {
                        _source.StageCounter(ProfileRunId, FrameId, SpanId, ParentSpanId, Stage, id, _counts[id]);
                    }
                }
            }

            if (_events)
            {
                if (Stage == 8)
                {
                    _source.PassEnd(ProfileRunId, FrameId, SpanId, ParentSpanId, Stage, _passIndex, _outcome);
                }
                else
                {
                    _source.StageEnd(ProfileRunId, FrameId, SpanId, ParentSpanId, Stage, _outcome);
                }
            }
        }

        internal void Start()
        {
            if (_events)
            {
                if (Stage == 8)
                {
                    _source.PassBegin(ProfileRunId, FrameId, SpanId, ParentSpanId, Stage, _passIndex);
                }
                else
                {
                    _source.StageBegin(ProfileRunId, FrameId, SpanId, ParentSpanId, Stage);
                }
            }
        }

        internal void Complete() => _outcome = 0;

        internal void Fail(Exception exception)
        {
            if (!_disposed)
            {
                _outcome = exception is OperationCanceledException ? 1 : 2;
            }
        }

        internal void Add(int id, long value)
        {
            if (_counts is not null)
            {
                _present |= 1U << id;
                _counts[id] += value;
            }
        }
    }
}
