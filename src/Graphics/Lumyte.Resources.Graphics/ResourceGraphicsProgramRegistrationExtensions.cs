using Lumyte.Resources.DependencyInjection;

namespace Lumyte.Resources.Graphics;

/// <summary>Registers CPU artifact pairs and shared GPU programs for constructor-injected shader inputs.</summary>
public static class ResourceGraphicsProgramRegistrationExtensions
{
    /// <summary>Adds program pipelines using the default graphics runtime.</summary>
    /// <param name="builder">The manager's pipeline configuration.</param>
    /// <returns>The same configuration.</returns>
    /// <remarks>Use UseGraphicsShaders&lt;GraphicsProgramResource&gt; and register an artifact pipeline, for example AddShaderPipeline.</remarks>
    public static ResourceRegistrationBuilder AddGraphicsPrograms(this ResourceRegistrationBuilder builder)
        => builder.AddSourcePipeline<GraphicsProgramArtifacts, GraphicsProgramArtifactStage>()
            .AddTransformPipeline<GraphicsProgramArtifacts, GraphicsProgramResource, GraphicsProgramStage>()
            .UseStageRuntime<GraphicsProgramResource, GraphicsResourceRuntime>();
}
