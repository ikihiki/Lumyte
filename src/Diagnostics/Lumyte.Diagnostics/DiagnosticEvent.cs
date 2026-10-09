namespace Lumyte.Diagnostics;

/// <summary>A detached event suitable for transport adapters.</summary>
/// <param name="Kind">The Kind argument.</param>
/// <param name="Timestamp">The Timestamp argument.</param>
/// <param name="Name">The Name argument.</param>
/// <param name="Value">The Value argument.</param>
/// <param name="TraceId">The TraceId argument.</param>
/// <param name="SpanId">The SpanId argument.</param>
/// <param name="ParentSpanId">The ParentSpanId argument.</param>
/// <param name="DurationTicks">The DurationTicks argument.</param>
/// <param name="Fields">The Fields argument.</param>
public sealed record DiagnosticEvent(string Kind, long Timestamp, string Name, DiagnosticValue Value, string? TraceId, string? SpanId, string? ParentSpanId, long DurationTicks, IReadOnlyDictionary<string, DiagnosticValue> Fields);
