namespace Lumyte.Resources.Graphics.Drawing2D;

/// <summary>Builds shared renderer resources through the selected graphics device epoch.</summary>
public sealed class Renderer2DResourceStage : IResourceTransform<Renderer2DArtifacts, Renderer2DResource, GraphicsResourceRuntime>
{
    private readonly GraphicsProgramStage _programs = new();

    /// <inheritdoc/>
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Resident;

    /// <inheritdoc/>
    public ResourceAddress GetInput(ResourceAddress output) => output;

    /// <inheritdoc/>
    public async ValueTask<Renderer2DResource> BuildAsync(Renderer2DArtifacts input, GraphicsResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
        => new(await _programs.BuildAsync(input.Program, runtime, context, cancellationToken));

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(Renderer2DResource resource, GraphicsResourceRuntime runtime)
        => _programs.ReleaseAsync(resource.Program, runtime);
}
