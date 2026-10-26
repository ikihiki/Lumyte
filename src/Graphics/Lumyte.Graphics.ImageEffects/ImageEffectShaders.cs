using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Supplies immutable shader artifacts, loaded directly or borrowed from resource leases.</summary>
/// <remarks>Retain resource-backed artifacts through graph preparation and GPU completion lifetimes.</remarks>
public sealed class ImageEffectShaders(ShaderArtifact fullscreen, ShaderArtifact effects)
{
    private static readonly Lazy<ImageEffectShaders> _embedded = new(static () => new(
        ShaderArtifact.LoadEmbedded(typeof(ImageEffectShaders).Assembly, "Lumyte.ImageEffects.fullscreen.lshader"),
        ShaderArtifact.LoadEmbedded(typeof(ImageEffectShaders).Assembly, "Lumyte.ImageEffects.effects.lshader")));

    /// <summary>Initializes a new instance of the <see cref="ImageEffectShaders"/> class borrowing existing modules.</summary>
    /// <param name="fullscreen">The borrowed fullscreen vertex module.</param>
    /// <param name="effects">The borrowed effect fragment module on the same device.</param>
    public ImageEffectShaders(IGraphicsShader fullscreen, IGraphicsShader effects)
        : this(fullscreen.Artifact, effects.Artifact)
    {
        FullscreenShader = fullscreen;
        EffectsShader = effects;
    }

    /// <summary>Initializes a new instance of the <see cref="ImageEffectShaders"/> class borrowing shared resources.</summary>
    /// <param name="pipeline">The pipeline implementing the image-effects ABI.</param>
    /// <param name="sampler">The borrowed nearest-clamp sampler.</param>
    public ImageEffectShaders(IGraphicsPipeline pipeline, IGraphicsSampler sampler)
        : this(pipeline.Desc.VertexShader, pipeline.Desc.FragmentShader ?? throw new ArgumentException("Image effects require a fragment shader.", nameof(pipeline)))
    {
        Pipeline = pipeline;
        Sampler = sampler;
    }

    /// <summary>Gets the directly embedded defaults without requiring a resource manager.</summary>
    public static ImageEffectShaders Embedded => _embedded.Value;

    /// <summary>Gets the fullscreen vertex artifact.</summary>
    public ShaderArtifact Fullscreen { get; } = fullscreen;

    /// <summary>Gets the effect fragment artifact.</summary>
    public ShaderArtifact Effects { get; } = effects;

    internal IGraphicsPipeline? Pipeline { get; }

    internal IGraphicsSampler? Sampler { get; }

    internal IGraphicsShader? FullscreenShader { get; }

    internal IGraphicsShader? EffectsShader { get; }
}
