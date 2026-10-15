namespace Lumyte.Memory;

/// <summary>Describes one committed memory charge.</summary>
/// <param name="Bytes">The committed byte count.</param>
/// <param name="Accuracy">Whether the byte count is measured or estimated.</param>
public readonly record struct MemoryUsage(ulong Bytes, MemoryAccuracy Accuracy);
