namespace Lumyte.Graphics.Native.Shaders;

/// <summary>How an artifact translates descriptor indices into the Native heap layout.</summary>
public enum NativeShaderDescriptorHeapAbiKind
{
    None,
    DirectX12,
    VulkanUnified,
    VulkanFixed,
}

/// <summary>Versioned descriptor addressing. DirectX 12 increments are opaque, never byte strides.</summary>
public readonly record struct NativeShaderDescriptorHeapAbi(
    NativeShaderDescriptorHeapAbiKind Kind,
    uint Version = 1,
    NativeGpuDescriptorLimits? Layout = null)
{
    public static NativeShaderDescriptorHeapAbi None => new(NativeShaderDescriptorHeapAbiKind.None);
    public static NativeShaderDescriptorHeapAbi DirectX12 => new(NativeShaderDescriptorHeapAbiKind.DirectX12);

    /// <summary>Device-derived unified resource stride, as emitted by Slang's unified descriptor heap option.</summary>
    public static NativeShaderDescriptorHeapAbi VulkanUnified => new(NativeShaderDescriptorHeapAbiKind.VulkanUnified);

    /// <summary>An artifact that embeds the supplied exact descriptor sizes, alignments, and strides.</summary>
    public static NativeShaderDescriptorHeapAbi VulkanFixed(NativeGpuDescriptorLimits layout)
        => new(NativeShaderDescriptorHeapAbiKind.VulkanFixed, Layout: layout);

    internal bool Matches(NativeShaderTarget target, NativeGpuDescriptorLimits? actual)
    {
        if (Version != 1) { return false; }
        return Kind switch
        {
            NativeShaderDescriptorHeapAbiKind.None => Layout is null,
            NativeShaderDescriptorHeapAbiKind.DirectX12 => target == NativeShaderTarget.DirectX12 && actual is null && Layout is null,
            NativeShaderDescriptorHeapAbiKind.VulkanFixed => target == NativeShaderTarget.Vulkan && Layout is { } expected && actual == expected,
            NativeShaderDescriptorHeapAbiKind.VulkanUnified => target == NativeShaderTarget.Vulkan && Layout is null &&
                actual is { } value && MatchesUnified(value),
            _ => false,
        };
    }

    private static bool MatchesUnified(NativeGpuDescriptorLimits value)
    {
        // Match Slang's actual ArrayStrideIdEXT expressions. Native heap padding
        // need not equal max(sizeof(image), sizeof(buffer)); do not silently round it.
        return value.ResourceSlotStride == Math.Max(value.ImageDescriptorSize, value.BufferDescriptorSize) &&
               value.SamplerSlotStride == value.SamplerDescriptorSize;
    }
}
