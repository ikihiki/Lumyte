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

    /// <summary>Creates a logical argument table.</summary>
    /// <param name="desc">The independent registration capacities.</param>
    /// <returns>The owned table.</returns>
    IArgumentTable CreateArgumentTable(ArgumentTableDesc desc);

    /// <summary>Resolves a caller-owned logical type's reflected wire layout.</summary>
    /// <typeparam name="T">The logical element type.</typeparam>
    /// <param name="shader">The shader handle.</param>
    /// <returns>The matching wire layout.</returns>
    ShaderDataLayout<T> GetDataLayout<T>(object shader);

    /// <summary>Packs logical elements and records their descriptor dependencies in a CPU Upload range.</summary>
    /// <typeparam name="T">The logical element type.</typeparam>
    /// <param name="destination">The exact-size Upload range.</param>
    /// <param name="values">The values to serialize.</param>
    /// <param name="schema">The reflected wire schema.</param>
    /// <param name="serializer">The explicit caller-owned serializer.</param>
    void PackShaderData<T>(BufferRange destination, ReadOnlySpan<T> values, object schema, IShaderDataSerializer<T> serializer);

    /// <summary>References registered completed shader data elements.</summary>
    /// <param name="range">The element-aligned range.</param>
    /// <param name="dataType">The matching logical element type.</param>
    /// <returns>The opaque root range.</returns>
    object CreateShaderDataReference(BufferRange range, Type dataType);

    /// <summary>Collects root dependencies and constructs automatic drawing bindings.</summary>
    /// <param name="pipeline">The matching pipeline handle.</param>
    /// <param name="data">The opaque shader-data root.</param>
    /// <returns>The leased automatic drawing arguments.</returns>
    object CreateDrawingArguments(object pipeline, object? data);

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
    object CreateArguments(object handle, IGpuRef<uint> data);
}
