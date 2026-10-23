using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;

namespace Lumyte.Resources.Audio;

/// <summary>Creates and releases an audio clip through its runtime owner.</summary>
public sealed class AudioClipStage : IResourceTransform<AudioPcmData, IAudioClip>
{
    /// <inheritdoc/>
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    /// <inheritdoc/>
    public ResourceAddress GetInput(ResourceAddress output) => output;

    /// <inheritdoc/>
    public ValueTask<IAudioClip> BuildAsync(AudioPcmData input, ResourceBuildContext context, CancellationToken cancellationToken)
        => ((AudioResourceRuntime)context.Runtime).CreateClipAsync(input, context, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask ReleaseAsync(IAudioClip resource) => await new AudioClip(resource).DestroyClip();
}
