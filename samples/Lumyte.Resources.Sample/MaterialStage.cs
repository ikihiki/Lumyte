namespace Lumyte.Resources.Sample;

internal sealed class MaterialStage(SampleCounters counters) : IResourceTransform<SceneDocument, MaterialInfo>
{
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    public ResourceAddress GetInput(ResourceAddress output) => new(output.Id);

    public ValueTask<MaterialInfo> BuildAsync(SceneDocument input, ResourceBuildContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Address.Subresource != "material/0")
        {
            throw new ArgumentException("This sample document defines material/0.", nameof(context));
        }

        string name = input.Root.GetProperty("materials")[0].GetProperty("name").GetString()
            ?? throw new InvalidDataException("A material name is required.");
        counters.RecordProjectionBuild();
        return ValueTask.FromResult(new MaterialInfo(name));
    }

    public ValueTask ReleaseAsync(MaterialInfo resource) => ValueTask.CompletedTask;
}
