using System.Reflection;

namespace Lumyte.Graphics;

/// <summary>
/// Owns backend-independent GPU resources and command submission.
/// </summary>
public sealed class GraphicsDevice : IDisposable
{
    private readonly IGraphicsDriver _driver;

    internal GraphicsDevice(IGraphicsDriver driver)
    {
        _driver = driver;
    }

    /// <summary>
    /// Gets the maximum supported buffer allocation size in bytes.
    /// </summary>
    public ulong MaxBufferSize => _driver.MaxBufferSize;

    /// <summary>
    /// Resolves the element stride and GPU copy alignment without allocating a buffer.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
    /// <returns>The backend-resolved element stride and copy requirements.</returns>
    public BufferLayout<T> GetBufferLayout<T>()
        where T : unmanaged => _driver.GetBufferLayout<T>();

    /// <summary>
    /// Allocates a typed buffer with the specified count, usage, and memory preference.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The owned typed buffer allocation.</returns>
    public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
        where T : unmanaged => _driver.CreateBuffer(desc);

    /// <summary>
    /// Allocates a single-mip, single-layer RGBA8 texture with explicit usage.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The owned texture allocation.</returns>
    public IGraphicsTexture CreateTexture(TextureDesc desc) => _driver.CreateTexture(desc);

    /// <summary>
    /// Creates an immutable sampler after validating its filtering and address modes.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The owned immutable sampler.</returns>
    public Sampler CreateSampler(SamplerDesc desc) => new(_driver, _driver.CreateSampler(desc));

    /// <summary>
    /// Creates an opaque, non-owning texture-view and sampler pair from this device.
    /// </summary>
    /// <param name="texture">The sampled texture view from this device.</param>
    /// <param name="sampler">The immutable sampler from this device.</param>
    /// <returns>The non-owning sampled pair, without exposing a physical binding.</returns>
    public SampledTexture2DReference CreateSampledTexture2DReference(IGraphicsTextureView texture, Sampler sampler) => new(_driver.CreateSampledTextureReference(texture, sampler));

    /// <summary>
    /// Snapshots logical materials and retains their finite sampled-resource set.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <param name="materials">The logical material snapshot, packed set, or opaque material range.</param>
    /// <returns>The owned snapshot and its retained sampled-resource set.</returns>
    public IGraphicsMaterialBindings CreateMaterialBindings(MaterialBindingsDesc desc, ReadOnlySpan<MaterialData> materials) => _driver.CreateMaterialBindings(desc, materials);

    /// <summary>
    /// References a registered, complete material range after explicit upload completion; does not wait or submit.
    /// </summary>
    /// <param name="range">The complete registered material range.</param>
    /// <returns>The non-owning reference to the completed, registered material range.</returns>
    public MaterialBufferReference CreateMaterialReference(BufferSlice<byte> range) => new(_driver.CreateMaterialReference(range.Range));

    /// <summary>
    /// Loads WGSL and optional Slang reflection from assembly resources; no shader compilation occurs.
    /// </summary>
    /// <param name="assembly">The assembly containing the embedded shader and reflection resources.</param>
    /// <param name="resourceName">The manifest name of the embedded WGSL resource.</param>
    /// <returns>The owned shader module loaded from the embedded resources.</returns>
    public ShaderModule CreateShader(Assembly assembly, string resourceName) => new(_driver, _driver.CreateShader(assembly, resourceName));

    /// <summary>
    /// Creates a compute pipeline for the specified shader entry point.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The owned compute pipeline.</returns>
    public ComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => new(_driver, _driver.CreateComputePipeline(desc));

    /// <summary>
    /// Creates a triangle-list pipeline with one RGBA8Unorm target and no depth or blending.
    /// </summary>
    /// <param name="desc">The immutable creation or recording settings to validate.</param>
    /// <returns>The owned graphics pipeline.</returns>
    public GraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => new(_driver, _driver.CreateGraphicsPipeline(desc));

    /// <summary>
    /// Creates a non-owning typed data reference after validating device, schema, and range.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type; shader ABI compatibility is validated separately.</typeparam>
    /// <param name="data">The non-owning storage range to reference.</param>
    /// <returns>The non-owning typed reference to the validated buffer range.</returns>
    public GpuReference<T> CreateReference<T>(BufferSlice<T> data)
        where T : unmanaged => new(_driver.CreateReference<T>(data.Range));

    /// <summary>
    /// Creates an encoder for explicit copy, compute, and render commands.
    /// </summary>
    /// <returns>The owned command recording scope.</returns>
    public CommandEncoder CreateCommandEncoder() => new(_driver, _driver.CreateCommandEncoder());

    /// <summary>
    /// Submits a finished command buffer once and returns its completion handle.
    /// </summary>
    /// <param name="commands">The finished, unsubmitted command buffer.</param>
    /// <returns>The completion handle for the explicitly submitted work.</returns>
    public Submission Submit(CommandBuffer commands) => new(_driver, _driver.Submit(commands));

    /// <summary>
    /// Releases an idle device; live resources, encoders, or submissions prevent disposal.
    /// </summary>
    public void Dispose() => _driver.Dispose();
}
