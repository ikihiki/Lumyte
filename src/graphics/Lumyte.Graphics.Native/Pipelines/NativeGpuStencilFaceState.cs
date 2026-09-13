namespace Lumyte.Graphics.Native;

public readonly record struct NativeGpuStencilFaceState(
    GpuCompareOp Compare = GpuCompareOp.Always,
    NativeGpuStencilOperation FailOp = NativeGpuStencilOperation.Keep,
    NativeGpuStencilOperation DepthFailOp = NativeGpuStencilOperation.Keep,
    NativeGpuStencilOperation PassOp = NativeGpuStencilOperation.Keep,
    uint Reference = 0)
{
    public NativeGpuStencilFaceState() : this(Compare: GpuCompareOp.Always) { }
}
