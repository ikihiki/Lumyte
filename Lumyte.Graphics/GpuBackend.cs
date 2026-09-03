namespace Lumyte.Graphics;

[Flags]
public enum GpuBackendCapabilities
{
    None = 0,
    ExplicitPlacement = 1 << 0,
    RasterPipeline = 1 << 1,
    DeviceOwnedResources = 1 << 2,
    MemoryAliasing = 1 << 3,
}

/// <summary>
/// Unified GPU backend contract. Capabilities identify operations that a backend can
/// implement honestly; unsupported operations throw <see cref="NotSupportedException"/>.
/// </summary>
public interface IGpuBackend : IDisposable
{
    void IDisposable.Dispose() { }

    GpuBackendCapabilities Capabilities { get; }

    IGpuQueue MainQueue => throw Unsupported(nameof(MainQueue));

    GpuMemoryAllocation AllocateMemory(ulong size, ulong alignment, GpuMemoryKind kind)
        => throw Unsupported(nameof(AllocateMemory));

    GpuMemoryAllocation AllocateMemory(
        ulong size,
        ulong alignment,
        GpuMemoryKind kind,
        ulong compatibility)
    {
        if (compatibility != 0)
        {
            throw Unsupported($"{nameof(AllocateMemory)} with a compatibility key");
        }
        return AllocateMemory(size, alignment, kind);
    }

    /// <summary>
    /// Combines two native memory-requirement compatibility values. The default treats them as
    /// opaque exact-match keys; bit-mask backends may return a non-empty intersection.
    /// </summary>
    bool TryCombineMemoryCompatibility(ulong left, ulong right, out ulong combined)
    {
        combined = left;
        return left == right;
    }

    /// <summary>Returns the stable arena block key selected for a compatibility requirement.</summary>
    ulong GetMemoryCompatibilityKey(GpuMemoryKind kind, ulong compatibility) => compatibility;

    bool IsMemoryCompatibilityKeyCompatible(
        GpuMemoryKind kind,
        ulong allocationKey,
        ulong requirement)
        => allocationKey == GetMemoryCompatibilityKey(kind, requirement);

    void FreeMemory(GpuMemoryAllocation allocation)
        => throw Unsupported(nameof(FreeMemory));

    GpuTextureMemoryRequirements GetTextureMemoryRequirements(GpuTextureDescription description)
        => throw Unsupported(nameof(GetTextureMemoryRequirements));

    GpuTextureHandle CreatePlacedTexture(
        GpuTextureDescription description,
        GpuMemoryAllocation allocation)
        => throw Unsupported(nameof(CreatePlacedTexture));

    GpuTextureHandle CreateTexture(GpuTextureDescription description)
        => throw Unsupported(nameof(CreateTexture));

    void DestroyTexture(GpuTextureHandle texture)
        => throw Unsupported(nameof(DestroyTexture));

    GpuTextureView CreateTextureView(
        GpuTextureHandle texture,
        GpuTextureViewDescription description)
        => throw Unsupported(nameof(CreateTextureView));

    void DestroyTextureView(GpuTextureView view)
        => throw Unsupported(nameof(DestroyTextureView));

    SamplerId CreateSampler(GpuSamplerDescription description)
        => throw Unsupported(nameof(CreateSampler));

    void DestroySampler(SamplerId sampler)
        => throw Unsupported(nameof(DestroySampler));

    void WriteTexture(
        GpuTextureHandle texture,
        ReadOnlySpan<byte> source,
        GpuTextureCopyFootprint footprint)
        => throw Unsupported(nameof(WriteTexture));

    byte[] ReadTexture(GpuTextureHandle texture, GpuTextureCopyFootprint footprint)
        => throw Unsupported(nameof(ReadTexture));

    GpuBufferMemoryRequirements GetBufferMemoryRequirements(GpuBufferDescription description)
        => throw Unsupported(nameof(GetBufferMemoryRequirements));

    GpuBufferHandle CreatePlacedBuffer(
        GpuBufferDescription description,
        GpuMemoryAllocation allocation)
        => throw Unsupported(nameof(CreatePlacedBuffer));

    GpuBufferHandle CreateBuffer(GpuBufferDescription description)
        => throw Unsupported(nameof(CreateBuffer));

    void WriteBuffer(GpuBufferHandle buffer, ReadOnlySpan<byte> source)
        => throw Unsupported(nameof(WriteBuffer));

    byte[] ReadBuffer(GpuBufferHandle buffer)
        => throw Unsupported(nameof(ReadBuffer));

    GpuMemoryAddress GetBufferMemoryAddress(GpuBufferHandle buffer, ulong offset, ulong length)
        => throw Unsupported(nameof(GetBufferMemoryAddress));

    void DestroyBuffer(GpuBufferHandle buffer)
        => throw Unsupported(nameof(DestroyBuffer));

    GpuBufferView CreateBufferView(
        GpuBufferHandle buffer,
        GpuBufferViewDescription description)
        => throw Unsupported(nameof(CreateBufferView));

    void DestroyBufferView(GpuBufferView view)
        => throw Unsupported(nameof(DestroyBufferView));

    GpuRasterPipelineHandle CreateRasterPipeline(
        GpuRasterPipelineDescription description,
        GpuShaderPackage package,
        string vertexEntryPoint,
        string pixelEntryPoint,
        ReadOnlyMemory<byte> expectedAbiHash)
        => throw Unsupported(nameof(CreateRasterPipeline));

    void DestroyRasterPipeline(GpuRasterPipelineHandle pipeline)
        => throw Unsupported(nameof(DestroyRasterPipeline));

    GpuComputePipelineHandle CreateComputePipeline(
        GpuShaderPackage package,
        string entryPoint,
        ReadOnlyMemory<byte> expectedAbiHash)
        => throw Unsupported(nameof(CreateComputePipeline));

    void DestroyComputePipeline(GpuComputePipelineHandle pipeline)
        => throw Unsupported(nameof(DestroyComputePipeline));

    private static NotSupportedException Unsupported(string operation)
        => new($"The GPU backend does not support {operation}.");
}
