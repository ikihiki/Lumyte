namespace Lumyte.Graphics.Presentation.Benchmarks;

internal sealed class FrameSample
{
    public int LogicalFrame { get; set; }

    public long InputId { get; set; }

    public string ContentId { get; set; } = string.Empty;

    public string? LogicalInputSha256 { get; set; }

    public object? UiCounters { get; set; }

    public long UpdateTicks { get; set; }

    public long LayoutTicks { get; set; }

    public long RecordTicks { get; set; }

    public long PrepareTicks { get; set; }

    public int IssuedDrawCalls { get; set; }

    public int CompositeDrawCalls { get; set; }

    public int RecordedCommandCount { get; set; }

    public long SlotWaitAndRetirementTicks { get; set; }

    public long AcquireTicks { get; set; }

    public long EncodeTicks { get; set; }

    public long InputAppliedTick { get; set; }

    public long SubmitStartTick { get; set; }

    public long SubmitTicks { get; set; }

    public long HostFrameToSubmitTicks { get; set; }

    public long PresentTicks { get; set; }

    public long PresentReturnTick { get; set; }

    public long? PresentReturnIntervalTicks { get; set; }

    public long CompletionObservedTick { get; set; }

    public long SubmitToCompletionObservationTicks => CompletionObservedTick - SubmitStartTick;

    public long InputToPresentReturnTicks => PresentReturnTick - InputAppliedTick;
}
