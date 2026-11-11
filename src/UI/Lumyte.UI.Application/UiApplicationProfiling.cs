using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace Lumyte.UI.Application;

[EventSource(Name = "Lumyte-UI-Application-Profiling")]
internal sealed class UiApplicationProfiling : EventSource
{
    internal static readonly UiApplicationProfiling Log = new();
    private static readonly AsyncLocal<Scope?> _current = new();
    private static long _nextSpan;

    [Event(1, Level = EventLevel.Informational, Keywords = Keywords.Stages)]
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
    public unsafe void StageEnd(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int outcome, int reason)
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
        data[5].DataPointer = (IntPtr)(&outcome);
        data[5].Size = sizeof(int);
        data[6].DataPointer = (IntPtr)(&reason);
        data[6].Size = sizeof(int);
        WriteEventCore(2, 7, data);
    }

    [Event(3, Level = EventLevel.Informational, Keywords = Keywords.Counters)]
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

    [Event(4, Level = EventLevel.Informational, Keywords = Keywords.Control)]
    public unsafe void FrameAccepted(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, Guid sourceId, long graphicsGeneration)
    {
        if (!IsEnabled(EventLevel.Informational, Keywords.Control))
        {
            return;
        }

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
        data[5].DataPointer = (IntPtr)(&sourceId);
        data[5].Size = sizeof(Guid);
        data[6].DataPointer = (IntPtr)(&graphicsGeneration);
        data[6].Size = sizeof(long);
        WriteEventCore(4, 7, data);
    }

    [Event(5, Level = EventLevel.Informational, Keywords = Keywords.Control)]
    public unsafe void FrameFinished(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int outcome, bool presented, Guid sourceId)
    {
        if (!IsEnabled(EventLevel.Informational, Keywords.Control))
        {
            return;
        }

        int presentedValue = presented ? 1 : 0;
        EventData* data = stackalloc EventData[8];
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
        data[6].DataPointer = (IntPtr)(&presentedValue);
        data[6].Size = sizeof(int);
        data[7].DataPointer = (IntPtr)(&sourceId);
        data[7].Size = sizeof(Guid);
        WriteEventCore(5, 8, data);
    }

    [Event(6, Level = EventLevel.Informational, Keywords = Keywords.Control)]
    public unsafe void FrameSuppressed(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int reason, long count)
    {
        if (!IsEnabled(EventLevel.Informational, Keywords.Control))
        {
            return;
        }

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
        data[5].DataPointer = (IntPtr)(&reason);
        data[5].Size = sizeof(int);
        data[6].DataPointer = (IntPtr)(&count);
        data[6].Size = sizeof(long);
        WriteEventCore(6, 7, data);
    }

    [Event(7, Level = EventLevel.Informational, Keywords = Keywords.Control)]
    public unsafe void SurfaceResult(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int kind, int status, int attempt)
    {
        if (!IsEnabled(EventLevel.Informational, Keywords.Control))
        {
            return;
        }

        EventData* data = stackalloc EventData[8];
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
        data[5].DataPointer = (IntPtr)(&kind);
        data[5].Size = sizeof(int);
        data[6].DataPointer = (IntPtr)(&status);
        data[6].Size = sizeof(int);
        data[7].DataPointer = (IntPtr)(&attempt);
        data[7].Size = sizeof(int);
        WriteEventCore(7, 8, data);
    }

    [Event(8, Level = EventLevel.Informational, Keywords = Keywords.Control)]
    public unsafe void DeviceRetired(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage)
    {
        if (!IsEnabled(EventLevel.Informational, Keywords.Control))
        {
            return;
        }

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
        WriteEventCore(8, 5, data);
    }

    [Event(9, Level = EventLevel.Informational, Keywords = Keywords.Control)]
    public unsafe void FrameResourcesReleased(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int reason)
    {
        if (!IsEnabled(EventLevel.Informational, Keywords.Control))
        {
            return;
        }

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
        data[5].DataPointer = (IntPtr)(&reason);
        data[5].Size = sizeof(int);
        WriteEventCore(9, 6, data);
    }

    [Event(10, Level = EventLevel.Informational, Keywords = Keywords.Control)]
    public unsafe void OwnerRelation(long rootSpanId, Guid owner)
    {
        if (!IsEnabled(EventLevel.Informational, Keywords.Control))
        {
            return;
        }

        EventData* data = stackalloc EventData[2];
        data[0].DataPointer = (IntPtr)(&rootSpanId);
        data[0].Size = sizeof(long);
        data[1].DataPointer = (IntPtr)(&owner);
        data[1].Size = sizeof(Guid);
        WriteEventCore(10, 2, data);
    }

    [Event(11, Level = EventLevel.Informational, Keywords = Keywords.Control)]
    public void RunMetadata(Guid profileRunId, string runtime, string operatingSystem, string processArchitecture, string backend, string assemblyVersion, string gpu, string buildHash, string seed, string scenario, string warmup)
    {
        if (IsEnabled(EventLevel.Informational, Keywords.Control))
        {
            WriteEvent(11, profileRunId, runtime, operatingSystem, processArchitecture, backend, assemblyVersion, gpu, buildHash, seed, scenario, warmup);
        }
    }

    [NonEvent]
    internal static Key ReadKey()
    {
        for (Activity? activity = Activity.Current; activity is not null; activity = activity.Parent)
        {
            if (activity.GetCustomProperty("Lumyte.Profiling.RunId") is Guid run && activity.GetCustomProperty("Lumyte.Profiling.FrameId") is long frame)
            {
                return new(run, frame);
            }
        }

        return default;
    }

    [NonEvent]
    internal static Scope? Begin(int stage, Key? key = null)
    {
        if (!Log.IsEnabled(EventLevel.Informational, Keywords.Stages | Keywords.Counters))
        {
            return null;
        }

        return new(stage, key ?? ReadKey());
    }

    internal readonly record struct Key(Guid Run, long Frame);

    public static class Keywords
    {
        public const EventKeywords Control = (EventKeywords)1;
        public const EventKeywords Stages = (EventKeywords)2;
        public const EventKeywords Counters = (EventKeywords)4;
        public const EventKeywords GPU = (EventKeywords)8;
    }

    internal sealed class Scope : IDisposable
    {
        private readonly Scope? _previous;
        private readonly bool _stages;
        private readonly bool _counters;
        private bool _disposed;

        internal Scope(int stage, Key key)
        {
            Stage = stage;
            Key = key;
            _previous = _current.Value;
            Parent = _previous is { } parent && parent.Key == key ? parent.Span : 0;
            Span = Interlocked.Increment(ref _nextSpan);
            _stages = Log.IsEnabled(EventLevel.Informational, Keywords.Stages);
            _counters = Log.IsEnabled(EventLevel.Informational, Keywords.Counters);
            _current.Value = this;
            if (_stages)
            {
                Log.StageBegin(key.Run, key.Frame, Span, Parent, stage);
            }
        }

        internal Key Key { get; }

        internal long Span { get; }

        internal long Parent { get; }

        internal int Stage { get; }

        internal int Outcome { get; set; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(_current.Value, this))
            {
                _current.Value = _previous;
            }

            if (_stages)
            {
                Log.StageEnd(Key.Run, Key.Frame, Span, Parent, Stage, Outcome, 0);
            }
        }

        internal void Failed(Exception error) => Outcome = error is OperationCanceledException ? 1 : 2;

        internal void Detach()
        {
            if (ReferenceEquals(_current.Value, this))
            {
                _current.Value = _previous;
            }
        }

        internal void FailAndDispose(Exception error)
        {
            Failed(error);
            Dispose();
        }

        internal void Counter(int id, long value)
        {
            if (_counters)
            {
                Log.StageCounter(Key.Run, Key.Frame, Span, Parent, Stage, id, value);
            }
        }
    }
}
