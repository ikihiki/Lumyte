using Lumyte.Audio.Abstractions;

namespace Lumyte.Resources.Audio;

/// <summary>Creates and releases an audio clip through its runtime owner.</summary>
public sealed class AudioClipStage : IResourceTransform<AudioPcmData, IAudioClip, AudioResourceRuntime>
{
    /// <inheritdoc/>
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Build;

    /// <inheritdoc/>
    public ResourceAddress GetInput(ResourceAddress output) => output;

    /// <inheritdoc/>
    public ValueTask<IAudioClip> BuildAsync(AudioPcmData input, AudioResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
        => runtime.CreateClipAsync(input, context, cancellationToken);

    /// <inheritdoc/>
    public ValueTask ReleaseAsync(IAudioClip resource, AudioResourceRuntime runtime) => runtime.DestroyClipAsync(resource);
}
