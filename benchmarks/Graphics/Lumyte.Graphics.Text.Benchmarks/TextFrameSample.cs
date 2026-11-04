namespace Lumyte.Graphics.Text.Benchmarks;

internal sealed record TextFrameSample(int FrameId, int State, int Slot, double SlotWaitMs, double PrepareHostMs, double RecordHostMs, double EncodeHostMs, double SubmitHostMs, double CompletionObservationMs, double RetirementMs, double HostIncludingSlotWaitMs, double? GpuSpanMs);
