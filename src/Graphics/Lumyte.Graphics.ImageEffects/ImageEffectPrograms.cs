using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.ImageEffects;

/// <summary>Identifies the fixed image-effect shader ABI and its optional embedded source.</summary>
public static class ImageEffectPrograms
{
    /// <summary>The compatible fullscreen vertex, EffectArguments, and effect-operation ABI.</summary>
    public const string ProgramId = "Lumyte.ImageEffects.fullscreen.v1";

    /// <summary>Gets the stateless embedded provider; each acquisition owns fresh native objects.</summary>
    public static IGraphicsProgramProvider DefaultProvider { get; } = new EmbeddedGraphicsProgramProvider(typeof(ImageEffectPrograms).Assembly, ProgramId, "Lumyte.ImageEffects.fullscreen.lshader", "Lumyte.ImageEffects.effects.lshader");
}
