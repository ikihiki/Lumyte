namespace Lumyte.Graphics.Native;

/// <summary>Limits exposed by the implemented Native compute and descriptor ABI.</summary>
public readonly record struct NativeGpuLimits(
    uint MaxRootDataSize,
    NativeGpuDispatchLimits Dispatch,
    NativeGpuDescriptorLimits? Descriptors = null);
