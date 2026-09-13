namespace Lumyte.Graphics.Native;

public readonly record struct NativeGpuColorAttachment(
    NativeGpuRenderViewHandle View,
    NativeGpuLoadOp LoadOp = NativeGpuLoadOp.Load,
    NativeGpuStoreOp StoreOp = NativeGpuStoreOp.Store,
    GpuClearColor ClearColor = default);
