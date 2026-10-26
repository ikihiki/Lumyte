using Lumyte.Graphics.ImageEffects;
using Lumyte.Resources.DependencyInjection;

namespace Lumyte.Resources.Graphics.ImageEffects;

/// <summary>Registers managed image-effect shaders without changing the application's source configuration.</summary>
public static class ResourceImageEffectRegistrationExtensions
{
    /// <summary>Adds the CPU shader pair and shared GPU modules using the default graphics environment.</summary>
    /// <param name="builder">The singleton manager's pipeline configuration.</param>
    /// <returns>The same configuration.</returns>
    /// <remarks>Use UseGraphicsShaders&lt;ImageEffectShaderResource&gt; to select custom inputs and register their artifact pipeline separately.</remarks>
    public static ResourceRegistrationBuilder AddImageEffects(this ResourceRegistrationBuilder builder)
        => builder.AddSourcePipeline<ImageEffectShaders, ImageEffectArtifactStage>()
            .AddTransformPipeline<ImageEffectShaders, ImageEffectShaderResource, ImageEffectShaderStage>()
            .UseStageRuntime<ImageEffectShaderResource, GraphicsResourceRuntime>();
}
