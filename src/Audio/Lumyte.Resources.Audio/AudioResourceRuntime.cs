using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;
using Lumyte.Execution;

namespace Lumyte.Resources.Audio;

/// <summary>Owns the resource-facing audio lifetime and dispatches creation to the audio owner.</summary>
/// <param name="audio">The same audio module collected by the execution runtime.</param>
public sealed class AudioResourceRuntime(AudioModule audio) : IExecutionModule, IResourceRuntimeDependency
{
    private readonly AudioModule _audio = audio;

    /// <inheritdoc/>
    public event EventHandler? Changed
    {
        add => _audio.Devices.StateChanged += value;
        remove => _audio.Devices.StateChanged -= value;
    }

    /// <inheritdoc/>
    public string Name => "ResourceAudio";

    /// <inheritdoc/>
    public string Id => "Audio";

    /// <inheritdoc/>
    public object Identity => _audio.Devices;

    /// <inheritdoc/>
    public ResourceRuntimeSnapshot Snapshot
    {
        get
        {
            AudioDeviceSnapshot state = _audio.Devices.State;
            return new(state.Version, state.Status == AudioDeviceServiceStatus.Ready, state.InvalidatedThroughVersion);
        }
    }

    /// <summary>Creates a fully initialized clip on the audio owner.</summary>
    /// <param name="data">The PCM whose storage remains alive until completion.</param>
    /// <param name="context">The construction recording this runtime version.</param>
    /// <param name="cancellationToken">Cancels before command execution.</param>
    /// <returns>The actual backend clip, never a placeholder.</returns>
    public async ValueTask<IAudioClip> CreateClipAsync(AudioPcmData data, ResourceBuildContext context, CancellationToken cancellationToken = default)
    {
        AudioDeviceLease lease = await context.UseAudioAsync(_audio.Devices, Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await _audio.State.CreateClipForLease(lease, data.Format, data.Samples, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a fully initialized voice on the audio owner.</summary>
    /// <param name="clip">The retained backend clip.</param>
    /// <param name="context">The construction recording this runtime version.</param>
    /// <param name="cancellationToken">Cancels before command execution.</param>
    /// <returns>The actual stopped backend voice.</returns>
    public async ValueTask<IAudioVoice> CreateVoiceAsync(IAudioClip clip, ResourceBuildContext context, CancellationToken cancellationToken = default)
    {
        AudioDeviceLease lease = await context.UseAudioAsync(_audio.Devices, Id, cancellationToken: cancellationToken).ConfigureAwait(false);
        return await _audio.State.CreateVoiceForLease(lease, clip, AudioBus.Effects, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Declare(ModuleBuilder builder) => builder.DependsOn<AudioModule>();

    /// <inheritdoc/>
    public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
    {
    }

    /// <inheritdoc/>
    public ValueTask StartAsync(ModuleContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;
}
