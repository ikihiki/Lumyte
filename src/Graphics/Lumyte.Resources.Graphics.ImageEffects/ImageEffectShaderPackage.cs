using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Graphics.ImageEffects;

/// <summary>Selects embedded image-effect shaders or two tracked artifact resources.</summary>
public sealed class ImageEffectShaderPackage
{
    /// <summary>Initializes a new instance of the <see cref="ImageEffectShaderPackage"/> class with tracked shader inputs.</summary>
    /// <param name="fullscreen">The fullscreen vertex artifact resource.</param>
    /// <param name="effects">The effect fragment artifact resource with the image-effects shader ABI.</param>
    public ImageEffectShaderPackage(ResourceKey<ShaderArtifact> fullscreen, ResourceKey<ShaderArtifact> effects)
    {
        Sources = (fullscreen, effects);
    }

    private ImageEffectShaderPackage()
    {
    }

    /// <summary>Gets the embedded defaults, without requiring source entries or user-defined names.</summary>
    public static ImageEffectShaderPackage Default { get; } = new();

    internal (ResourceKey<ShaderArtifact> Fullscreen, ResourceKey<ShaderArtifact> Effects)? Sources { get; }
}
