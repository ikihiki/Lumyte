namespace Lumyte.Diagnostics;

/// <summary>Frame metadata from the owning engine thread.</summary>
/// <param name="Number">The Number argument.</param>
/// <param name="TimestampTicks">The TimestampTicks argument.</param>
public readonly record struct DiagnosticFrame(long Number, long TimestampTicks);
