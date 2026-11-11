using System.Diagnostics.Tracing;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal sealed class ProfilingListener : EventListener
{
    private int _begins;
    private int _ends;
    private int _relations;
    private int _errors;

    internal void Verify()
    {
        if (_errors != 0 || _begins == 0 || _begins != _ends || _relations == 0)
        {
            throw new InvalidOperationException($"Trimmed profiling metadata failed: begin={_begins}, end={_ends}, relations={_relations}, errors={_errors}.");
        }
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "Lumyte-RenderGraph-Profiling")
        {
            EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)0x1F);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        switch (eventData.EventId)
        {
            case 0:
                Interlocked.Increment(ref _errors);
                break;
            case 1:
            case 5:
                Interlocked.Increment(ref _begins);
                break;
            case 2:
            case 6:
                Interlocked.Increment(ref _ends);
                break;
            case 4:
                if (eventData.Payload is { Count: 7 } values && values[0] is Guid && values[1] is Guid && values[2] is Guid && values[3] is long && values[4] is long && values[5] is int && values[6] is int)
                {
                    Interlocked.Increment(ref _relations);
                }
                else
                {
                    Interlocked.Increment(ref _errors);
                }

                break;
        }
    }
}
