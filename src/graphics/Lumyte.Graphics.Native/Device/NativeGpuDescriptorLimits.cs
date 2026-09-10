namespace Lumyte.Graphics.Native;

/// <summary>Byte-based descriptor layout for backends whose shader ABI addresses descriptor bytes.</summary>
/// <remarks>Opaque descriptor-handle increments are not byte sizes and are not represented by this type.</remarks>
public readonly record struct NativeGpuDescriptorLimits(
    ulong ResourceSlotStride,
    ulong SamplerSlotStride,
    ulong ImageDescriptorSize,
    ulong BufferDescriptorSize,
    ulong SamplerDescriptorSize,
    ulong ImageDescriptorAlignment,
    ulong BufferDescriptorAlignment,
    ulong SamplerDescriptorAlignment);
