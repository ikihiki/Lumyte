using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Effects.Benchmarks;

internal sealed record FrameSample(int FrameId, int Slot, double SlotWaitMs, double GraphBuildPrepareMs,
    double GraphCompileMs, double GraphRecordMs, double EncodeMs, double SubmitMs,
    double SubmitToCompletionObservationMs, double RetirementMs, double HostIncludingSlotWaitMs,
    double? GpuSpanMs, RenderGraphStatistics Statistics);
