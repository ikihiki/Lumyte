namespace Lumyte.Resources.Sample;

internal sealed class SceneStage : IResourceTransform<SceneDocument, SceneResource>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    public ResourceAddress GetInput(ResourceAddress output) => new(output.Id);

    public async ValueTask<SceneResource> BuildAsync(SceneDocument input, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        MeshInfo mesh = await context.RequireAsync(new ResourceKey<MeshInfo>(context.Address.Id, "mesh/0", "geometry:v1"), cancellationToken: cancellationToken);
        MaterialInfo material = await context.RequireAsync(new ResourceKey<MaterialInfo>(context.Address.Id, "material/0", "preview:v1"), cancellationToken: cancellationToken);
        return new(mesh, material);
    }

    public ValueTask ReleaseAsync(SceneResource resource) => ValueTask.CompletedTask;
}
