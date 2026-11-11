using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;

namespace Lumyte.UI;

[EventSource(Name = "Lumyte-UI-Profiling")]
internal sealed class UiProfiling : EventSource
{
    internal static readonly UiProfiling Log = new();
    [ThreadStatic]
    private static Scope? _current;
    private static long _nextSpan;

    [Event(1, Level = EventLevel.Informational, Keywords = Keywords.Stages)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "EventData contains only declared fixed-width Guid, Int32 and Int64 scalar fields; no object graph or reflected properties are serialized.")]
    public unsafe void StageBegin(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage)
    {
        EventData* data = stackalloc EventData[5];
        data[0].DataPointer = (IntPtr)(&profileRunId);
        data[0].Size = sizeof(Guid);
        data[1].DataPointer = (IntPtr)(&frameId);
        data[1].Size = sizeof(long);
        data[2].DataPointer = (IntPtr)(&spanId);
        data[2].Size = sizeof(long);
        data[3].DataPointer = (IntPtr)(&parentSpanId);
        data[3].Size = sizeof(long);
        data[4].DataPointer = (IntPtr)(&stage);
        data[4].Size = sizeof(int);
        WriteEventCore(1, 5, data);
    }

    [Event(2, Level = EventLevel.Informational, Keywords = Keywords.Stages)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "EventData contains only declared fixed-width Guid, Int32 and Int64 scalar fields; no object graph or reflected properties are serialized.")]
    public unsafe void StageEnd(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int outcome)
    {
        EventData* data = stackalloc EventData[6];
        data[0].DataPointer = (IntPtr)(&profileRunId);
        data[0].Size = sizeof(Guid);
        data[1].DataPointer = (IntPtr)(&frameId);
        data[1].Size = sizeof(long);
        data[2].DataPointer = (IntPtr)(&spanId);
        data[2].Size = sizeof(long);
        data[3].DataPointer = (IntPtr)(&parentSpanId);
        data[3].Size = sizeof(long);
        data[4].DataPointer = (IntPtr)(&stage);
        data[4].Size = sizeof(int);
        data[5].DataPointer = (IntPtr)(&outcome);
        data[5].Size = sizeof(int);
        WriteEventCore(2, 6, data);
    }

    [Event(3, Level = EventLevel.Informational, Keywords = Keywords.Counters)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "EventData contains only declared fixed-width Guid, Int32 and Int64 scalar fields; no object graph or reflected properties are serialized.")]
    public unsafe void StageCounter(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int counterId, long value)
    {
        EventData* data = stackalloc EventData[7];
        data[0].DataPointer = (IntPtr)(&profileRunId);
        data[0].Size = sizeof(Guid);
        data[1].DataPointer = (IntPtr)(&frameId);
        data[1].Size = sizeof(long);
        data[2].DataPointer = (IntPtr)(&spanId);
        data[2].Size = sizeof(long);
        data[3].DataPointer = (IntPtr)(&parentSpanId);
        data[3].Size = sizeof(long);
        data[4].DataPointer = (IntPtr)(&stage);
        data[4].Size = sizeof(int);
        data[5].DataPointer = (IntPtr)(&counterId);
        data[5].Size = sizeof(int);
        data[6].DataPointer = (IntPtr)(&value);
        data[6].Size = sizeof(long);
        WriteEventCore(3, 7, data);
    }

    [NonEvent]
    internal static Scope? Begin(int stage)
    {
        bool stages = Log.IsEnabled(EventLevel.Informational, Keywords.Stages);
        bool counters = Log.IsEnabled(EventLevel.Informational, Keywords.Counters);
        return stages || counters ? new Scope(stage, stages, counters) : null;
    }

    [NonEvent]
    internal static void Add(int stage, int counter, long value = 1)
    {
        if (_current is not { CountersEnabled: true })
        {
            return;
        }

        for (Scope? scope = _current; scope is not null; scope = scope.Parent)
        {
            // Capture refresh notifications are not input dispatch handler calls.
            if (stage == 4 && scope.Stage == 3)
            {
                return;
            }

            if (scope.Stage == stage)
            {
                scope.Add(counter, value);
                return;
            }
        }
    }

    public static class Keywords
    {
        public const EventKeywords Control = (EventKeywords)1;
        public const EventKeywords Stages = (EventKeywords)2;
        public const EventKeywords Counters = (EventKeywords)4;
        public const EventKeywords GPU = (EventKeywords)8;
    }

    internal sealed class Scope : IDisposable
    {
        private readonly Guid _runId;
        private readonly long _frameId;
        private readonly long _spanId;
        private readonly bool _stages;
        private readonly bool _counters;
        private long _first;
        private long _second;
        private long _third;
        private bool _consumedKnown;
        private int _outcome;

        internal Scope(int stage, bool stages, bool counters)
        {
            Stage = stage;
            Parent = _current;
            _stages = stages;
            _counters = counters;
            _spanId = Interlocked.Increment(ref _nextSpan);
            for (Activity? activity = Activity.Current; activity is not null; activity = activity.Parent)
            {
                if (activity.GetCustomProperty("Lumyte.Profiling.RunId") is Guid runId
                    && activity.GetCustomProperty("Lumyte.Profiling.FrameId") is long frameId)
                {
                    _runId = runId;
                    _frameId = frameId;
                    break;
                }
            }

            _current = this;
            if (_stages)
            {
                Log.StageBegin(_runId, _frameId, _spanId, Parent?._spanId ?? 0, Stage);
            }
        }

        internal Scope? Parent { get; }

        internal int Stage { get; }

        internal bool CountersEnabled => _counters;

        public void Dispose()
        {
            _current = Parent;
            if (_counters)
            {
                Counter(1, _first);
                if (Stage is 1 or 2 or 4 or 6)
                {
                    Counter(2, _second);
                }

                if (Stage == 2 || (Stage == 4 && _consumedKnown && _outcome == 0))
                {
                    Counter(3, _third);
                }
            }

            if (_stages)
            {
                Log.StageEnd(_runId, _frameId, _spanId, Parent?._spanId ?? 0, Stage, _outcome);
            }
        }

        internal void Skip() => _outcome = 3;

        internal void Fail(Exception error) => _outcome = error is OperationCanceledException ? 1 : 2;

        internal void Consumed(bool consumed)
        {
            _third = consumed ? 1 : 0;
            _consumedKnown = true;
        }

        internal void Add(int counter, long value)
        {
            if (!_counters)
            {
                return;
            }

            switch (counter % 100)
            {
                case 1:
                    _first += value;
                    break;
                case 2:
                    _second += value;
                    break;
                case 3:
                    _third += value;
                    break;
            }
        }

        private void Counter(int offset, long value) =>
            Log.StageCounter(_runId, _frameId, _spanId, Parent?._spanId ?? 0, Stage, (Stage * 100) + offset, value);
    }
}
