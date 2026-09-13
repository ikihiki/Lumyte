namespace Lumyte.Graphics.Native;

public readonly record struct NativeGpuColorTargetDescription(
    GpuFormat Format,
    GpuColorWriteMask WriteMask = GpuColorWriteMask.All,
    NativeGpuBlendDescription Blend = default);
