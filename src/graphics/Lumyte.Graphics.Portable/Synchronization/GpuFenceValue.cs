namespace Lumyte.Graphics.Portable;

/// <summary>A non-owning timeline identity and its full-width signal value.</summary>
public readonly record struct GpuFenceValue(GpuSemaphore Semaphore, ulong Value);
