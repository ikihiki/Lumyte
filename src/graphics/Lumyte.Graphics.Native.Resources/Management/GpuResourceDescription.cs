namespace Lumyte.Graphics.Native.Resources;

public readonly record struct GpuBufferDescription(ulong Size,
    NativeGpuMemoryKind MemoryKind = NativeGpuMemoryKind.GpuOnly, ulong Alignment = 1);
public enum GpuTextureViewPurpose { Sampled, Storage, Attachment }
public readonly record struct GpuTextureViewDescription(NativeGpuTextureViewDimension Dimension, GpuFormat Format,
    NativeGpuTextureAspect Aspect, uint BaseMip, uint MipCount, uint BaseLayer, uint LayerCount,
    GpuTextureViewPurpose Purpose = GpuTextureViewPurpose.Sampled,
    NativeGpuRenderViewFlags RenderFlags = NativeGpuRenderViewFlags.None);
public readonly record struct GpuBufferViewDescription(ulong Offset, ulong Length,
    NativeGpuBufferAccess Access = NativeGpuBufferAccess.ReadOnly);
public sealed record GpuResourceManagerOptions(ulong BlockSize = 16 * 1024 * 1024,
    uint ResourceDescriptorCapacity = 4096, uint SamplerDescriptorCapacity = 256);
public readonly record struct GpuResourceStatistics(int ResourceCount, int HeldResourceCount,
    int PendingSubmissionCount, ulong AllocatedBytes, uint ResourceDescriptorCount, uint SamplerDescriptorCount,
    int ViewCacheCount, int SamplerCacheCount);
