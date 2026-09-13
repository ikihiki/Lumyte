using P = Lumyte.Graphics.Portable;
using N = WebGpuSharp;
using F = WebGpuSharp.FFI;

namespace Lumyte.Graphics.WebGPU;

public sealed partial class WebGpuBackend
{
    private static uint? EncodeLimit(uint? value, string name)
    {
        if (value == uint.MaxValue) { throw new ArgumentOutOfRangeException(name, "This value is reserved for an unspecified C API limit."); }
        return value;
    }

    private static ulong? EncodeLimit(ulong? value, string name)
    {
        if (value == ulong.MaxValue) { throw new ArgumentOutOfRangeException(name, "This value is reserved for an unspecified C API limit."); }
        return value;
    }

    internal static unsafe N.Limits MapRequiredLimits(P.GpuRequiredLimits requested, uint availableImmediateSize)
        => new()
        {
            MaxTextureDimension1D = EncodeLimit(requested.MaxTextureDimension1D, nameof(requested.MaxTextureDimension1D)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxTextureDimension2D = EncodeLimit(requested.MaxTextureDimension2D, nameof(requested.MaxTextureDimension2D)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxTextureDimension3D = EncodeLimit(requested.MaxTextureDimension3D, nameof(requested.MaxTextureDimension3D)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxTextureArrayLayers = EncodeLimit(requested.MaxTextureArrayLayers, nameof(requested.MaxTextureArrayLayers)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxBindGroups = EncodeLimit(requested.MaxBindGroups, nameof(requested.MaxBindGroups)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxBindingsPerBindGroup = EncodeLimit(requested.MaxBindingsPerBindGroup, nameof(requested.MaxBindingsPerBindGroup)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxDynamicUniformBuffersPerPipelineLayout = EncodeLimit(requested.MaxDynamicUniformBuffersPerPipelineLayout, nameof(requested.MaxDynamicUniformBuffersPerPipelineLayout)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxDynamicStorageBuffersPerPipelineLayout = EncodeLimit(requested.MaxDynamicStorageBuffersPerPipelineLayout, nameof(requested.MaxDynamicStorageBuffersPerPipelineLayout)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxSampledTexturesPerShaderStage = EncodeLimit(requested.MaxSampledTexturesPerShaderStage, nameof(requested.MaxSampledTexturesPerShaderStage)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxSamplersPerShaderStage = EncodeLimit(requested.MaxSamplersPerShaderStage, nameof(requested.MaxSamplersPerShaderStage)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxStorageBuffersPerShaderStage = EncodeLimit(requested.MaxStorageBuffersPerShaderStage, nameof(requested.MaxStorageBuffersPerShaderStage)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxStorageTexturesPerShaderStage = EncodeLimit(requested.MaxStorageTexturesPerShaderStage, nameof(requested.MaxStorageTexturesPerShaderStage)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxUniformBuffersPerShaderStage = EncodeLimit(requested.MaxUniformBuffersPerShaderStage, nameof(requested.MaxUniformBuffersPerShaderStage)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MinUniformBufferOffsetAlignment = EncodeLimit(requested.MinUniformBufferOffsetAlignment, nameof(requested.MinUniformBufferOffsetAlignment)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MinStorageBufferOffsetAlignment = EncodeLimit(requested.MinStorageBufferOffsetAlignment, nameof(requested.MinStorageBufferOffsetAlignment)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxColorAttachments = EncodeLimit(requested.MaxColorAttachments, nameof(requested.MaxColorAttachments)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxColorAttachmentBytesPerSample = EncodeLimit(requested.MaxColorAttachmentBytesPerSample, nameof(requested.MaxColorAttachmentBytesPerSample)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxComputeWorkgroupStorageSize = EncodeLimit(requested.MaxComputeWorkgroupStorageSize, nameof(requested.MaxComputeWorkgroupStorageSize)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxComputeInvocationsPerWorkgroup = EncodeLimit(requested.MaxComputeInvocationsPerWorkgroup, nameof(requested.MaxComputeInvocationsPerWorkgroup)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxComputeWorkgroupSizeX = EncodeLimit(requested.MaxComputeWorkgroupSizeX, nameof(requested.MaxComputeWorkgroupSizeX)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxComputeWorkgroupSizeY = EncodeLimit(requested.MaxComputeWorkgroupSizeY, nameof(requested.MaxComputeWorkgroupSizeY)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxComputeWorkgroupSizeZ = EncodeLimit(requested.MaxComputeWorkgroupSizeZ, nameof(requested.MaxComputeWorkgroupSizeZ)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxComputeWorkgroupsPerDimension = EncodeLimit(requested.MaxComputeWorkgroupsPerDimension, nameof(requested.MaxComputeWorkgroupsPerDimension)) ?? F.WebGPU_FFI.LIMIT_U32_UNDEFINED,
            MaxImmediateSize = EncodeLimit(requested.MaxImmediateSize, nameof(requested.MaxImmediateSize)) ?? availableImmediateSize,
            MaxBufferSize = EncodeLimit(requested.MaxBufferSize, nameof(requested.MaxBufferSize)) ?? F.WebGPU_FFI.LIMIT_U64_UNDEFINED,
            MaxUniformBufferBindingSize = EncodeLimit(requested.MaxUniformBufferBindingSize, nameof(requested.MaxUniformBufferBindingSize)) ?? F.WebGPU_FFI.LIMIT_U64_UNDEFINED,
            MaxStorageBufferBindingSize = EncodeLimit(requested.MaxStorageBufferBindingSize, nameof(requested.MaxStorageBufferBindingSize)) ?? F.WebGPU_FFI.LIMIT_U64_UNDEFINED,
        };

    internal static unsafe P.GpuDeviceLimits MapEffectiveLimits(N.Limits limits)
        => new()
        {
            MaxTextureDimension1D = limits.MaxTextureDimension1D,
            MaxTextureDimension2D = limits.MaxTextureDimension2D,
            MaxTextureDimension3D = limits.MaxTextureDimension3D,
            MaxTextureArrayLayers = limits.MaxTextureArrayLayers,
            MaxBindGroups = limits.MaxBindGroups,
            MaxBindingsPerBindGroup = limits.MaxBindingsPerBindGroup,
            MaxDynamicUniformBuffersPerPipelineLayout = limits.MaxDynamicUniformBuffersPerPipelineLayout,
            MaxDynamicStorageBuffersPerPipelineLayout = limits.MaxDynamicStorageBuffersPerPipelineLayout,
            MaxSampledTexturesPerShaderStage = limits.MaxSampledTexturesPerShaderStage,
            MaxSamplersPerShaderStage = limits.MaxSamplersPerShaderStage,
            MaxStorageBuffersPerShaderStage = limits.MaxStorageBuffersPerShaderStage,
            MaxStorageTexturesPerShaderStage = limits.MaxStorageTexturesPerShaderStage,
            MaxUniformBuffersPerShaderStage = limits.MaxUniformBuffersPerShaderStage,
            MinUniformBufferOffsetAlignment = limits.MinUniformBufferOffsetAlignment,
            MinStorageBufferOffsetAlignment = limits.MinStorageBufferOffsetAlignment,
            MaxColorAttachments = limits.MaxColorAttachments,
            MaxColorAttachmentBytesPerSample = limits.MaxColorAttachmentBytesPerSample,
            MaxComputeWorkgroupStorageSize = limits.MaxComputeWorkgroupStorageSize,
            MaxComputeInvocationsPerWorkgroup = limits.MaxComputeInvocationsPerWorkgroup,
            MaxComputeWorkgroupSizeX = limits.MaxComputeWorkgroupSizeX,
            MaxComputeWorkgroupSizeY = limits.MaxComputeWorkgroupSizeY,
            MaxComputeWorkgroupSizeZ = limits.MaxComputeWorkgroupSizeZ,
            MaxComputeWorkgroupsPerDimension = limits.MaxComputeWorkgroupsPerDimension,
            MaxImmediateSize = limits.MaxImmediateSize,
            MaxBufferSize = limits.MaxBufferSize,
            MaxUniformBufferBindingSize = limits.MaxUniformBufferBindingSize,
            MaxStorageBufferBindingSize = limits.MaxStorageBufferBindingSize,
        };
}
