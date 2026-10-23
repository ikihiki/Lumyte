using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;

namespace Lumyte.Resources.Audio;

/// <summary>Creates and releases an audio voice through its runtime owner.</summary>
public sealed class AudioVoiceStage : IResourceTransform<IAudioClip, IAudioVoice, AudioResourceRuntime>
{
    /// <inheritdoc/>
    public ResourceDependencyLifetime InputLifetime => ResourceDependencyLifetime.Resident;

    /// <inheritdoc/>
    public ResourceAddress GetInput(ResourceAddress output) => output;

    /// <inheritdoc/>
    public ValueTask<IAudioVoice> BuildAsync(IAudioClip input, AudioResourceRuntime runtime, ResourceBuildContext context, CancellationToken cancellationToken)
        => runtime.CreateVoiceAsync(input, context, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask ReleaseAsync(IAudioVoice resource) => await new AudioVoice(resource).Destroy();
}
