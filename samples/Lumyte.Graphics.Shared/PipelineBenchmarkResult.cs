namespace Lumyte.Graphics.Samples;

/// <summary>CPU wall-clock timings for the pipeline comparison workload.</summary>
/// <param name="DrawCount">The repeated draw count, excluding the first draw.</param>
/// <param name="FirstDrawMilliseconds">First draw recording including first pipeline creation.</param>
/// <param name="RepeatedDrawsMilliseconds">Repeated recording including pipeline resolution or creation.</param>
/// <param name="CompletionMilliseconds">Submission through GPU completion, including CPU scheduling.</param>
/// <param name="DestructionMilliseconds">Program disposal including native pipeline destruction.</param>
public sealed record PipelineBenchmarkResult(int DrawCount, double FirstDrawMilliseconds, double RepeatedDrawsMilliseconds, double CompletionMilliseconds, double DestructionMilliseconds);
