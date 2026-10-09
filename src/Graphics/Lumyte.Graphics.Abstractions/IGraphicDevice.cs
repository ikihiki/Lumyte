namespace Lumyte.Graphics.Abstractions;

/// <summary>Provides immutable capabilities of a backend-owned graphics device.</summary>
/// <remarks>Concurrent operations are not synchronized; the caller manages all required resource lifetime and access synchronization.</remarks>
public interface IGraphicDevice
{
    /// <summary>Gets the immutable device capabilities.</summary>
    public DeviceCaps Caps { get; }

    /// <summary>Gets the device-owned general queue.</summary>
    IGraphicsQueue Queue { get; }

    /// <summary>Gets the raw element layout and GPU copy alignment expressed for T.</summary>
    /// <typeparam name="T">The unmanaged storage element type.</typeparam>
    /// <returns>The backend-resolved layout without implicit padding.</returns>
    BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged;

    /// <summary>Creates an owned typed buffer using the exact requested element count.</summary>
    /// <typeparam name="T">The unmanaged storage element type.</typeparam>
    /// <param name="desc">The immutable count, usage and memory requirements.</param>
    /// <returns>The concrete backend allocation through its common interface.</returns>
    IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged;

    /// <summary>Creates an owned two-dimensional texture with the exact requested attributes.</summary>
    /// <param name="desc">The immutable storage format, usages, dimensions and subresources.</param>
    /// <returns>The concrete backend allocation through its common interface.</returns>
    IGraphicsTexture CreateTexture(TextureDesc desc);

    /// <summary>Creates an owned sampler using the exact requested state.</summary>
    /// <param name="desc">The immutable filters, addressing, LOD, anisotropy and comparison state.</param>
    /// <returns>The concrete backend sampler through the common interface.</returns>
    IGraphicsSampler CreateSampler(SamplerDesc desc);

    /// <summary>Creates an owned logical registration table without constructing shader bindings.</summary>
    /// <param name="desc">The independent logical capacities and optional diagnostic label.</param>
    /// <returns>The backend table owning its active resource registrations.</returns>
    IArgumentTable CreateArgumentTable(ArgumentTableDesc desc);

    /// <summary>Creates an owned native shader module without compiling source or submitting commands.</summary>
    /// <param name="artifact">The code and reflection matching the backend target.</param>
    /// <returns>The concrete backend shader through its common interface.</returns>
    IGraphicsShader CreateShader(ShaderArtifact artifact);

    /// <summary>Creates a one-shot command buffer in Recording state.</summary>
    /// <param name="desc">The diagnostic description.</param>
    /// <returns>The owned recording.</returns>
    IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc);

    /// <summary>Gets exact copy constraints without padding or allocation.</summary>
    /// <param name="format">The color storage format.</param>
    /// <returns>The resolved texture copy layout.</returns>
    TextureCopyLayout GetTextureCopyLayout(TextureFormat format);

    /// <summary>Creates a shader program; draw state and attachment formats are resolved at draw.</summary>
    /// <param name="desc">The program description.</param>
    /// <returns>The owned backend program.</returns>
    IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc);

    /// <summary>Creates a compute program from embedded entry and workgroup metadata.</summary>
    /// <param name="desc">The compute description.</param>
    /// <returns>The owned executable program.</returns>
    IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc);
}
