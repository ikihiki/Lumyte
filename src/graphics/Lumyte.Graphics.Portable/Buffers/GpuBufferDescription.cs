namespace Lumyte.Graphics.Portable;

/// <summary>Logical byte size and intended uses. The runtime validates supported combinations.</summary>
public readonly record struct GpuBufferDescription(ulong Size, GpuBufferUsage Usage);
