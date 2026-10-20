namespace Lumyte.Resources.Gltf.Sample;

internal sealed class SceneUploadStage : IResourceTransform<CpuScene, GpuScene>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    public ResourceAddress GetInput(ResourceAddress output) => output;

    public async ValueTask<GpuScene> BuildAsync(CpuScene input, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        var draws = new List<SceneDraw>();
        foreach (CpuSceneNode root in input.Roots)
        {
            await UploadAsync(root, context, draws, cancellationToken);
        }

        return new(draws.AsReadOnly());
    }

    public ValueTask ReleaseAsync(GpuScene resource) => ValueTask.CompletedTask;

    private static async ValueTask UploadAsync(CpuSceneNode node, ResourceBuildContext context, List<SceneDraw> draws, CancellationToken cancellationToken)
    {
        if (node.MeshKey is { } meshKey)
        {
            GpuMesh mesh = await context.RequireAsync(new ResourceKey<GpuMesh>(meshKey.Address), cancellationToken: cancellationToken);
            draws.Add(new(mesh, node.World));
        }

        foreach (CpuSceneNode child in node.Children)
        {
            await UploadAsync(child, context, draws, cancellationToken);
        }
    }
}
