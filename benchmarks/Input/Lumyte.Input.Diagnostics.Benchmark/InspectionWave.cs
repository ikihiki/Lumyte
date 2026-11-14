namespace Lumyte.Input.Diagnostics.Benchmark;

/// <summary>Detached observations from a real consumer wave.</summary>
/// <param name="Snapshot">The observed snapshot.</param>
/// <param name="Values">The observed values.</param>
/// <param name="Accepted">The observed accepted.</param>
/// <param name="Rejected">The observed rejected.</param>
/// <param name="Expired">The observed expired.</param>
/// <param name="Error">The observed error.</param>
/// <param name="Revision">The observed revision.</param>
/// <param name="Commands">The observed commands.</param>
/// <param name="StopReason">The observed stopreason.</param>
public sealed record InspectionWave(string Snapshot, float[] Values, int Accepted, int Rejected, int Expired, string? Error, long Revision, int Commands, string? StopReason);
