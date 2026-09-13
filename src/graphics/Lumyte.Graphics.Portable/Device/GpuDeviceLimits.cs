namespace Lumyte.Graphics.Portable;

/// <summary>Effective limits returned by the created device, without substituting adapter maxima.</summary>
public sealed record GpuDeviceLimits
{
    public uint MaxTextureDimension1D { get; init; }
    public uint MaxTextureDimension2D { get; init; }
    public uint MaxTextureDimension3D { get; init; }
    public uint MaxTextureArrayLayers { get; init; }
    public uint MaxBindGroups { get; init; }
    public uint MaxBindingsPerBindGroup { get; init; }
    public uint MaxDynamicUniformBuffersPerPipelineLayout { get; init; }
    public uint MaxDynamicStorageBuffersPerPipelineLayout { get; init; }
    public uint MaxSampledTexturesPerShaderStage { get; init; }
    public uint MaxSamplersPerShaderStage { get; init; }
    public uint MaxStorageBuffersPerShaderStage { get; init; }
    public uint MaxStorageTexturesPerShaderStage { get; init; }
    public uint MaxUniformBuffersPerShaderStage { get; init; }
    public uint MinUniformBufferOffsetAlignment { get; init; }
    public uint MinStorageBufferOffsetAlignment { get; init; }
    public uint MaxColorAttachments { get; init; }
    public uint MaxColorAttachmentBytesPerSample { get; init; }
    public uint MaxComputeWorkgroupStorageSize { get; init; }
    public uint MaxComputeInvocationsPerWorkgroup { get; init; }
    public uint MaxComputeWorkgroupSizeX { get; init; }
    public uint MaxComputeWorkgroupSizeY { get; init; }
    public uint MaxComputeWorkgroupSizeZ { get; init; }
    public uint MaxComputeWorkgroupsPerDimension { get; init; }
    public uint MaxImmediateSize { get; init; }
    public ulong MaxBufferSize { get; init; }
    public ulong MaxUniformBufferBindingSize { get; init; }
    public ulong MaxStorageBufferBindingSize { get; init; }
}
