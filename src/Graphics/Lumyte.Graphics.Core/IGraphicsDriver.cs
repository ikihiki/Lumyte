using System.Reflection;

namespace Lumyte.Graphics;

// Internal backend protocol; no backend types enter the public API.

/// <summary>
/// Provides the internal device and resource protocol without exposing backend types.
/// </summary>
internal interface IGraphicsDriver : IDisposable, ICommandBufferBackendContract
{
    /// <summary>
    /// Gets the maximum supported buffer allocation size in bytes.
    /// </summary>
    ulong MaxBufferSize { get; }

    /// <summary>
    /// Resolves the element stride and GPU copy alignment without allocating a buffer.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
    /// <returns>The backend-resolved element stride and copy requirements.</returns>
    BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged;

    /// <summary>
    /// Allocates a typed buffer with the specified count, usage, and memory preference.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The owned typed buffer allocation.</returns>
    IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged;

    /// <summary>
    /// Creates a non-owning typed data reference after validating device, schema, and range.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
    /// <param name="data">The non-owning storage range to reference.</param>
    /// <returns>The non-owning typed reference to the validated buffer range.</returns>
    object CreateReference<T>(BufferRange data)
        where T : unmanaged;

    /// <summary>
    /// Allocates a single-mip, single-layer RGBA8 texture with explicit usage.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The owned texture allocation.</returns>
    IGraphicsTexture CreateTexture(TextureDesc desc);

    /// <summary>
    /// Creates an immutable sampler after validating its filtering and address modes.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The owned immutable sampler.</returns>
    object CreateSampler(SamplerDesc desc);

    /// <summary>
    /// Creates an opaque, non-owning texture-view and sampler pair from this device.
    /// </summary>
    /// <param name="texture">The sampled texture view from this device.</param>
    /// <param name="sampler">The immutable sampler from this device.</param>
    /// <returns>The internal non-owning sampled-pair representation.</returns>
    object CreateSampledTextureReference(IGraphicsTextureView texture, Sampler sampler);

    /// <summary>
    /// Gets the caller-defined element layout validated against embedded Slang reflection; unsupported schemas are rejected.
    /// </summary>
    /// <param name="shader">The shader handle whose reflection defines the material schema.</param>
    /// <returns>The reflected element layout and compiled sampled-resource capacity.</returns>
    MaterialResourceLayout GetMaterialLayout(object shader);

    /// <summary>
    /// Serializes caller-defined logical elements into a snapshot and retains their finite sampled-resource set.
    /// </summary>
    /// <typeparam name="T">The caller-defined logical buffer element type.</typeparam>
    /// <param name="desc">The reflected layout and fallback resource settings.</param>
    /// <param name="materials">The logical elements to serialize into an immutable snapshot.</param>
    /// <param name="serializer">The caller-owned mapping from logical values to the Slang wire schema.</param>
    /// <returns>The owned snapshot and its retained sampled-resource set.</returns>
    IGraphicsMaterialBindings CreateMaterialBindings<T>(MaterialBindingsDesc desc, ReadOnlySpan<T> materials, IShaderDataSerializer<T> serializer);

    /// <summary>
    /// References a registered, complete material range after explicit upload completion; does not wait or submit.
    /// </summary>
    /// <param name="range">The complete registered material range.</param>
    /// <returns>The non-owning reference to the completed, registered material range.</returns>
    object CreateMaterialReference(BufferRange range);

    /// <summary>
    /// Creates leased drawing arguments for a complete material range with the same shader schema.
    /// </summary>
    /// <param name="pipeline">The matching pipeline from the same device.</param>
    /// <param name="materials">The logical material snapshot, packed set, or opaque material range.</param>
    /// <returns>The internal drawing arguments retaining the material range and its resources.</returns>
    object CreateMaterialArguments(object pipeline, MaterialBufferReference materials);

    /// <summary>
    /// Loads WGSL and optional Slang reflection from assembly resources; no shader compilation occurs.
    /// </summary>
    /// <param name="assembly">The assembly containing the embedded shader and reflection resources.</param>
    /// <param name="resourceName">The manifest name of the embedded WGSL resource.</param>
    /// <returns>The owned shader module loaded from the embedded resources.</returns>
    object CreateShader(Assembly assembly, string resourceName);

    /// <summary>
    /// Creates a compute pipeline for the specified shader entry point.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The owned compute pipeline.</returns>
    object CreateComputePipeline(ComputePipelineDesc desc);

    /// <summary>
    /// Creates a triangle-list pipeline with one RGBA8Unorm target and no depth or blending.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The owned graphics pipeline.</returns>
    object CreateGraphicsPipeline(GraphicsPipelineDesc desc);

    /// <summary>
    /// Releases the specified backend resource handle.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    void DisposeHandle(object handle);

    /// <summary>
    /// Creates leased compute arguments from a valid writable UInt32 storage range.
    /// </summary>
    /// <param name="handle">The internal backend handle owned by this device.</param>
    /// <param name="data">The non-owning storage range to reference.</param>
    /// <returns>The owned arguments retaining the pipeline and its referenced resources.</returns>
    object CreateArguments(object handle, GpuReference<uint> data);
}
