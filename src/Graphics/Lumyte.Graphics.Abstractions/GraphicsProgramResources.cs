namespace Lumyte.Graphics.Abstractions;

/// <summary>A borrowed, immutable bundle of one device generation's program and matching reflection artifact.</summary>
/// <param name="device">The exact physical device that created the native objects.</param>
/// <param name="pipeline">The compatible graphics pipeline, retained by the lease owner.</param>
/// <param name="fragmentArtifact">The matching fragment artifact used for shader-data layouts.</param>
/// <param name="defaultSampler">An optional borrowed shared sampler retained by the same owner.</param>
/// <remarks>The owner retains the pipeline and its shaders. A program identifier promises compatible argument layouts, shader-data layouts and instruction semantics; metadata validation alone does not establish semantic compatibility.</remarks>
public sealed class GraphicsProgramResources(IGraphicDevice device, IGraphicsPipeline pipeline, ShaderArtifact fragmentArtifact, IGraphicsSampler? defaultSampler = null)
{
    /// <summary>Gets the borrowed physical device.</summary>
    public IGraphicDevice Device { get; } = device ?? throw new ArgumentNullException(nameof(device));

    /// <summary>Gets the borrowed pipeline.</summary>
    public IGraphicsPipeline Pipeline { get; } = pipeline ?? throw new ArgumentNullException(nameof(pipeline));

    /// <summary>Gets the optional shared sampler; null lets the drawing preparation create one.</summary>
    public IGraphicsSampler? DefaultSampler { get; } = defaultSampler;

    /// <summary>Gets the immutable artifact matching the pipeline's fragment shader.</summary>
    public ShaderArtifact FragmentArtifact { get; } = fragmentArtifact ?? throw new ArgumentNullException(nameof(fragmentArtifact));
}
