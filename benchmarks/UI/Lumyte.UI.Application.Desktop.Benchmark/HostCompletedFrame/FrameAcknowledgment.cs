using System.Diagnostics.Tracing;

namespace Lumyte.UI.Application.Desktop.Benchmark.HostCompletedFrame;

/// <summary>Joins a newly accepted frame to its own successful finished event, never a previous flight.</summary>
public sealed class FrameAcknowledgment : EventListener
{
    private readonly object _gate = new();
    private TaskCompletionSource<long>? _pending;
    private Guid _source;
    private Guid _run;
    private long _frame;
    private long? _generation;
    private int _ownerThread;

    /// <summary>Arms on the UI owner after the model mutation and before that owner's next recording boundary.</summary>
    /// <returns>The newly accepted and completed frame identity.</returns>
    public Task<long> Arm()
    {
        lock (_gate)
        {
            if (_pending is not null)
            {
                throw new InvalidOperationException("Only one serial frame request may be pending.");
            }

            _frame = 0;
            _ownerThread = Environment.CurrentManagedThreadId;
            _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pending.Task;
        }
    }

    /// <summary>Consumes the public EventSource scalar contract; also supports deterministic join tests.</summary>
    /// <param name="accepted">True for FrameAccepted, false for FrameFinished.</param>
    /// <param name="run">The nonempty profiling run.</param>
    /// <param name="frame">The positive frame identity.</param>
    /// <param name="source">The window presentation source.</param>
    /// <param name="outcome">Zero is success.</param>
    /// <param name="presented">Whether presentation succeeded after GPU and surface-release waits.</param>
    /// <param name="generation">The actual graphics generation; recovery invalidates this hardware-audited fixture.</param>
    public void Observe(bool accepted, Guid run, long frame, Guid source, int outcome = 0, bool presented = false, long generation = 0)
    {
        lock (_gate)
        {
            if (_pending is null || run == Guid.Empty || source == Guid.Empty || frame <= 0 || (_source != Guid.Empty && source != _source))
            {
                return;
            }

            if (accepted)
            {
                // EventListener delivery is synchronous on the producing thread. Only this
                // Arm callback's UI owner may select a frame; concurrent headless hosts cannot match.
                if (Environment.CurrentManagedThreadId != _ownerThread)
                {
                    return;
                }

                if (_generation is { } previous && previous != generation)
                {
                    _pending.TrySetException(new InvalidOperationException("Graphics generation changed; repeat explicit adapter audit in a new fixture."));
                    _pending = null;
                    return;
                }

                _generation = generation;
                if (_frame == 0)
                {
                    (_source, _run, _frame) = (source, run, frame);
                }

                return;
            }

            if (_frame != frame || _run != run || _source != source)
            {
                return;
            }

            TaskCompletionSource<long> completion = _pending;
            _pending = null;
            if (outcome == 0 && presented)
            {
                completion.TrySetResult(frame);
            }
            else
            {
                completion.TrySetException(new InvalidOperationException($"Frame {frame} failed: outcome={outcome}, presented={presented}."));
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "Lumyte-UI-Application-Profiling")
        {
            // Control only: no per-stage/counter collection in the headline completion benchmark.
            EnableEvents(eventSource, EventLevel.Informational, (EventKeywords)1);
        }
    }

    /// <inheritdoc/>
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventSource.Name != "Lumyte-UI-Application-Profiling" || eventData.Payload is not { } payload)
        {
            return;
        }

        if (eventData.EventId == 4 && payload.Count == 7)
        {
            Observe(true, (Guid)payload[0]!, (long)payload[1]!, (Guid)payload[5]!, generation: (long)payload[6]!);
        }
        else if (eventData.EventId == 5 && payload.Count == 8)
        {
            Observe(false, (Guid)payload[0]!, (long)payload[1]!, (Guid)payload[7]!, (int)payload[5]!, (bool)payload[6]!);
        }
    }
}
