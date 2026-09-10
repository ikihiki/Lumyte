namespace Lumyte.Graphics.Native;

/// <summary>Dispatch group-count limits, distinct from the shader's local thread count.</summary>
public readonly record struct NativeGpuDispatchLimits(
    uint MaxGroupCountX, uint MaxGroupCountY, uint MaxGroupCountZ, ulong MaxTotalGroupCount);
