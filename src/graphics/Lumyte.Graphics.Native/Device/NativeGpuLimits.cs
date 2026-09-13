namespace Lumyte.Graphics.Native;

/// <summary>Limits exposed by the enabled Native shader and descriptor ABI.</summary>
public readonly record struct NativeGpuLimits(
    uint MaxRootDataSize,
    NativeGpuDispatchLimits Dispatch,
    NativeGpuDescriptorLimits? Descriptors = null,
    NativeGpuMeshShaderLimits? MeshShader = null);
