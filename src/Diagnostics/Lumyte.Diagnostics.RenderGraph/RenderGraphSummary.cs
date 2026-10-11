namespace Lumyte.Diagnostics.RenderGraph;

/// <summary>Fixed-size aggregate counters for this game scope, independent of captures.</summary>
/// <param name="Compiles">The number of successful compiles.</param>
/// <param name="Records">The number of successful recordings.</param>
/// <param name="Failures">The exact number of compile and record failures.</param>
/// <param name="CompileMilliseconds">The sum of successful compile CPU durations.</param>
/// <param name="CompileMaximumMilliseconds">The greatest successful compile CPU duration.</param>
/// <param name="RecordMilliseconds">The sum of successful recording CPU durations.</param>
/// <param name="RecordMaximumMilliseconds">The greatest successful recording CPU duration.</param>
/// <param name="LivePasses">The sum of live passes over successful compiles.</param>
/// <param name="CulledPasses">The sum of culled passes over successful compiles.</param>
/// <param name="Barriers">The sum of planned barriers over successful compiles.</param>
/// <param name="Captures">The number of pending and retained captures.</param>
/// <param name="CapturePending">Whether a capture is waiting for a matching compile.</param>
/// <param name="RetainedBytes">The estimated retained bytes, including reserved capture budgets.</param>
public sealed record RenderGraphSummary(long Compiles, long Records, long Failures, double CompileMilliseconds, double CompileMaximumMilliseconds, double RecordMilliseconds, double RecordMaximumMilliseconds, long LivePasses, long CulledPasses, long Barriers, long Captures, bool CapturePending, long RetainedBytes);
