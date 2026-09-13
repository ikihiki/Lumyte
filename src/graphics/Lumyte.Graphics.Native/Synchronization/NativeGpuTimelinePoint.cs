namespace Lumyte.Graphics.Native;

/// <summary>A non-owning semaphore identity and value used as a GPU dependency or completion point.</summary>
/// <remarks>Equal numeric values on different semaphores do not identify the same completion point.</remarks>
public readonly record struct NativeGpuTimelinePoint(NativeGpuSemaphore Semaphore, ulong Value);
