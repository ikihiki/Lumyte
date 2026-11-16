using System.Diagnostics.Tracing;

namespace Lumyte.UI.BrowserBenchmarkHost;

internal sealed class PresentedFrameListener : EventListener
{
    private TaskCompletionSource<long>? _pending;
    private Guid _acceptedRun;
    private Guid _source;
    private long _accepted;
    private long _latest;
    private long _floor;
    private int _lastEvent;
    private long _span;
    private long _parent;
    private int _stage;

    internal string Diagnostic => $"latest={_latest}; floor={_floor}; accepted={_accepted}; event={_lastEvent}";

    internal Task<long> Arm()
    {
        if (_pending is not null)
        {
            throw new InvalidOperationException("Only one update may be outstanding.");
        }

        _floor = _latest;
        _accepted = 0;
        _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return _pending.Task;
    }

    internal void Disarm() => _pending = null;

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "Lumyte-UI-Application-Profiling")
        {
            EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)1);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventSource.Name != "Lumyte-UI-Application-Profiling" || eventData.Payload is not { } values)
        {
            return;
        }

        if (eventData.EventId is 4 or 5 or 0)
        {
            _lastEvent = eventData.EventId;
        }

        if (eventData.EventId == 4)
        {
            long frame = Convert.ToInt64(values[1], System.Globalization.CultureInfo.InvariantCulture);
            _latest = frame;
            if (_pending is not null && _accepted == 0 && frame > _floor)
            {
                _acceptedRun = (Guid)values[0]!;
                _source = (Guid)values[5]!;
                _span = Convert.ToInt64(values[2], System.Globalization.CultureInfo.InvariantCulture);
                _parent = Convert.ToInt64(values[3], System.Globalization.CultureInfo.InvariantCulture);
                _stage = Convert.ToInt32(values[4], System.Globalization.CultureInfo.InvariantCulture);
                _accepted = frame;
            }
        }
        else if (eventData.EventId == 5 && _pending is { } pending && _accepted != 0
            && (Guid)values[0]! == _acceptedRun
            && Convert.ToInt64(values[1], System.Globalization.CultureInfo.InvariantCulture) == _accepted
            && (Guid)values[7]! == _source
            && Convert.ToInt64(values[2], System.Globalization.CultureInfo.InvariantCulture) == _span
            && Convert.ToInt64(values[3], System.Globalization.CultureInfo.InvariantCulture) == _parent
            && Convert.ToInt32(values[4], System.Globalization.CultureInfo.InvariantCulture) == _stage)
        {
            int outcome = Convert.ToInt32(values[5], System.Globalization.CultureInfo.InvariantCulture);
            bool presented = Convert.ToBoolean(values[6], System.Globalization.CultureInfo.InvariantCulture);
            if (outcome == 0 && presented)
            {
                pending.TrySetResult(_accepted);
            }
            else
            {
                pending.TrySetException(new InvalidOperationException($"Frame {_accepted} was not successfully presented: {outcome}."));
            }
        }
    }
}
