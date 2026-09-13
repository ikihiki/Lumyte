namespace Lumyte.Graphics.Portable;

/// <summary>Optional device creation requirements. Null leaves negotiation to the backend.</summary>
/// <remarks>
/// Max limits request at least this capacity; Min offset alignments request at most this restriction.
/// An unspecified MaxImmediateSize requests the adapter's available direct-input capacity, without a fixed byte ABI.
/// Required values are passed to the runtime for validation.
/// </remarks>
public sealed record GpuRequiredLimits
{
    public uint? MaxTextureDimension1D { get; init; }
    public uint? MaxTextureDimension2D { get; init; }
    public uint? MaxTextureDimension3D { get; init; }
    public uint? MaxTextureArrayLayers { get; init; }
    public uint? MaxBindGroups { get; init; }
    public uint? MaxBindingsPerBindGroup { get; init; }
    public uint? MaxDynamicUniformBuffersPerPipelineLayout { get; init; }
    public uint? MaxDynamicStorageBuffersPerPipelineLayout { get; init; }
    public uint? MaxSampledTexturesPerShaderStage { get; init; }
    public uint? MaxSamplersPerShaderStage { get; init; }
    public uint? MaxStorageBuffersPerShaderStage { get; init; }
    public uint? MaxStorageTexturesPerShaderStage { get; init; }
    public uint? MaxUniformBuffersPerShaderStage { get; init; }
    public uint? MinUniformBufferOffsetAlignment { get; init; }
    public uint? MinStorageBufferOffsetAlignment { get; init; }
    public uint? MaxColorAttachments { get; init; }
    public uint? MaxColorAttachmentBytesPerSample { get; init; }
    public uint? MaxComputeWorkgroupStorageSize { get; init; }
    public uint? MaxComputeInvocationsPerWorkgroup { get; init; }
    public uint? MaxComputeWorkgroupSizeX { get; init; }
    public uint? MaxComputeWorkgroupSizeY { get; init; }
    public uint? MaxComputeWorkgroupSizeZ { get; init; }
    public uint? MaxComputeWorkgroupsPerDimension { get; init; }
    public uint? MaxImmediateSize { get; init; }
    public ulong? MaxBufferSize { get; init; }
    public ulong? MaxUniformBufferBindingSize { get; init; }
    public ulong? MaxStorageBufferBindingSize { get; init; }
}
