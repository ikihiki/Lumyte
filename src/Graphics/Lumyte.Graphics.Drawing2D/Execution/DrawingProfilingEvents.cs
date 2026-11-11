using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;

namespace Lumyte.Graphics.Drawing2D;

[EventSource(Name = "Lumyte-Drawing2D-Profiling")]
internal sealed class DrawingProfilingEvents : EventSource
{
    internal static readonly DrawingProfilingEvents Log = new();

    [Event(1, Level = EventLevel.Informational, Keywords = Keywords.Stages)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "EventData contains only declared fixed-width Guid, Int32 and Int64 scalar fields; no object graph or reflected properties are serialized.")]
    public unsafe void StageBegin(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage)
    {
        EventData* data = stackalloc EventData[5];
        data[0].DataPointer = (IntPtr)(&profileRunId);
        data[0].Size = 16;
        data[1].DataPointer = (IntPtr)(&frameId);
        data[1].Size = 8;
        data[2].DataPointer = (IntPtr)(&spanId);
        data[2].Size = 8;
        data[3].DataPointer = (IntPtr)(&parentSpanId);
        data[3].Size = 8;
        data[4].DataPointer = (IntPtr)(&stage);
        data[4].Size = 4;
        WriteEventCore(1, 5, data);
    }

    [Event(2, Level = EventLevel.Informational, Keywords = Keywords.Stages)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "EventData contains only declared fixed-width Guid, Int32 and Int64 scalar fields; no object graph or reflected properties are serialized.")]
    public unsafe void StageEnd(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int outcome)
    {
        EventData* data = stackalloc EventData[6];
        data[0].DataPointer = (IntPtr)(&profileRunId);
        data[0].Size = 16;
        data[1].DataPointer = (IntPtr)(&frameId);
        data[1].Size = 8;
        data[2].DataPointer = (IntPtr)(&spanId);
        data[2].Size = 8;
        data[3].DataPointer = (IntPtr)(&parentSpanId);
        data[3].Size = 8;
        data[4].DataPointer = (IntPtr)(&stage);
        data[4].Size = 4;
        data[5].DataPointer = (IntPtr)(&outcome);
        data[5].Size = 4;
        WriteEventCore(2, 6, data);
    }

    [Event(3, Level = EventLevel.Informational, Keywords = Keywords.Counters)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "EventData contains only declared fixed-width Guid, Int32 and Int64 scalar fields; no object graph or reflected properties are serialized.")]
    public unsafe void StageCounter(Guid profileRunId, long frameId, long spanId, long parentSpanId, int stage, int counterId, long value)
    {
        EventData* data = stackalloc EventData[7];
        data[0].DataPointer = (IntPtr)(&profileRunId);
        data[0].Size = 16;
        data[1].DataPointer = (IntPtr)(&frameId);
        data[1].Size = 8;
        data[2].DataPointer = (IntPtr)(&spanId);
        data[2].Size = 8;
        data[3].DataPointer = (IntPtr)(&parentSpanId);
        data[3].Size = 8;
        data[4].DataPointer = (IntPtr)(&stage);
        data[4].Size = 4;
        data[5].DataPointer = (IntPtr)(&counterId);
        data[5].Size = 4;
        data[6].DataPointer = (IntPtr)(&value);
        data[6].Size = 8;
        WriteEventCore(3, 7, data);
    }

    [Event(4, Level = EventLevel.Informational, Keywords = Keywords.Stages)]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "EventData contains only declared fixed-width Guid, Int32 and Int64 scalar fields; no object graph or reflected properties are serialized.")]
    public unsafe void ReleaseOperation(Guid profileRunId, long frameId, long spanId, int operation)
    {
        EventData* data = stackalloc EventData[4];
        data[0].DataPointer = (IntPtr)(&profileRunId);
        data[0].Size = 16;
        data[1].DataPointer = (IntPtr)(&frameId);
        data[1].Size = 8;
        data[2].DataPointer = (IntPtr)(&spanId);
        data[2].Size = 8;
        data[3].DataPointer = (IntPtr)(&operation);
        data[3].Size = 4;
        WriteEventCore(4, 4, data);
    }

    public static class Keywords
    {
        public const EventKeywords Control = (EventKeywords)1;
        public const EventKeywords Stages = (EventKeywords)2;
        public const EventKeywords Counters = (EventKeywords)4;
        public const EventKeywords Gpu = (EventKeywords)8;
    }
}
