namespace Lumyte.Resources.Gltf.Sample;

internal sealed class BinaryStage : IResourceSourceStage<SourceBytes>
{
    public ValueTask<SourceBytes> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken) => SourceBytes.ReadAsync(context, cancellationToken);

    public ValueTask ReleaseAsync(SourceBytes resource) => ValueTask.CompletedTask;
}
