namespace Lumyte.Resources.Sample;

internal sealed class MeshStage(SampleCounters counters) : IResourceTransform<SceneDocument, MeshInfo>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    public ResourceAddress GetInput(ResourceAddress output) => new(output.Id);

    public ValueTask<MeshInfo> BuildAsync(SceneDocument input, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Address.Subresource != "mesh/0")
        {
            throw new ArgumentException("This sample document defines mesh/0.", nameof(context));
        }

        counters.RecordProjectionBuild();
        return ValueTask.FromResult(new MeshInfo(input.Root.GetProperty("meshes")[0].GetProperty("vertexCount").GetInt32()));
    }

    public ValueTask ReleaseAsync(MeshInfo resource) => ValueTask.CompletedTask;
}
