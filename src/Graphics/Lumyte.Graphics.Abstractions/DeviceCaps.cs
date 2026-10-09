namespace Lumyte.Graphics.Abstractions;

/// <summary>Contains backend-resolved capability values without owning GPU resources.</summary>
public sealed record DeviceCaps
{
    /// <summary>Gets the supported compiled shader target.</summary>
    public ShaderTarget ShaderTarget { get; init; }

    /// <summary>Gets the supported portable feature flags.</summary>
    public GraphicsFeatures Features { get; init; } = GraphicsFeatures.None;

    /// <summary>Gets the maximum buffer allocation size in bytes.</summary>
    public ulong MaxBufferSize { get; init; }

    /// <summary>Gets the maximum storage binding size in bytes.</summary>
    public ulong MaxStorageBufferBindingSize { get; init; }

    /// <summary>Gets the maximum two-dimensional texture extent in texels.</summary>
    public uint MaxTextureDimension2D { get; init; }

    /// <summary>Gets the maximum two-dimensional texture array layer count.</summary>
    public uint MaxTextureArrayLayers { get; init; }

    /// <summary>Gets the maximum color attachment count.</summary>
    public uint MaxColorAttachments { get; init; }

    /// <summary>Gets the maximum sampled texture count per shader stage.</summary>
    public uint MaxSampledTexturesPerStage { get; init; }

    /// <summary>Gets the maximum sampler count per shader stage.</summary>
    public uint MaxSamplersPerStage { get; init; }

    /// <summary>Gets the maximum supported sampler anisotropy; one means anisotropic filtering is unavailable.</summary>
    public ushort MaxSamplerAnisotropy { get; init; } = 1;

    /// <summary>Gets the maximum uniform buffer count per shader stage.</summary>
    public uint MaxUniformBuffersPerStage { get; init; }

    /// <summary>Gets the maximum storage buffer count per shader stage.</summary>
    public uint MaxStorageBuffersPerStage { get; init; }

    /// <summary>Gets the maximum compute invocations per workgroup.</summary>
    public uint MaxComputeInvocationsPerWorkgroup { get; init; }

    /// <summary>Gets the required GPU buffer copy offset alignment in bytes.</summary>
    public uint CopyBufferOffsetAlignment { get; init; } = 1;

    /// <summary>Gets the required GPU buffer copy length alignment in bytes.</summary>
    public uint CopyBufferSizeAlignment { get; init; } = 1;

    /// <summary>Gets the required encoded image copy row alignment in bytes.</summary>
    public uint CopyBytesPerRowAlignment { get; init; } = 1;

    /// <summary>Gets the required native storage binding offset alignment in bytes.</summary>
    public uint StorageBufferOffsetAlignment { get; init; } = 1;
}
