using Lumyte.Graphics.Abstractions;

namespace Lumyte.Resources.Gltf.Sample;

internal sealed class ShaderStage : IResourceSourceStage<ShaderArtifact>
{
    public async ValueTask<ShaderArtifact> BuildAsync(ResourceBuildContext context, CancellationToken cancellationToken)
    {
        SourceBytes bytes = await SourceBytes.ReadAsync(context, cancellationToken);
        return new(bytes.Buffer.Memory.Span);
    }

    public ValueTask ReleaseAsync(ShaderArtifact resource) => ValueTask.CompletedTask;
}
