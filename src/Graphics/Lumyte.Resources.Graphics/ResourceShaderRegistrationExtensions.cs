using Lumyte.Graphics.Abstractions;
using Lumyte.Resources.DependencyInjection;

namespace Lumyte.Resources.Graphics;

/// <summary>Registers shared CPU artifacts and device-dependent shader modules.</summary>
public static class ResourceShaderRegistrationExtensions
{
    /// <summary>Adds compiled shader loading and GPU creation using the default graphics environment.</summary>
    /// <param name="builder">The singleton manager's pipeline configuration.</param>
    /// <returns>The same configuration.</returns>
    public static ResourceRegistrationBuilder AddShaderPipeline(this ResourceRegistrationBuilder builder)
        => builder.AddSourcePipeline<ShaderArtifact, ShaderArtifactStage>()
            .AddTransformPipeline<ShaderArtifact, GraphicsShaderResource, GraphicsShaderStage>()
            .UseStageRuntime<GraphicsShaderResource, GraphicsResourceRuntime>();
}
