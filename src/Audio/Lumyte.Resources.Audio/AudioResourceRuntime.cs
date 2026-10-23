using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;
using Lumyte.Execution;

namespace Lumyte.Resources.Audio;

/// <summary>Owns the resource-facing audio lifetime and dispatches creation to the audio owner.</summary>
/// <param name="audio">The same audio module collected by the execution runtime.</param>
public sealed class AudioResourceRuntime(AudioModule audio) : IExecutionModule, IResourceRuntimeDependency
{
    private readonly AudioModule _audio = audio ?? throw new ArgumentNullException(nameof(audio));
    private readonly object _gate = new();
    private ResourceRuntimeSnapshot _snapshot;

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public string Name => "ResourceAudio";

    /// <inheritdoc/>
    public string Id => "Audio";

    /// <inheritdoc/>
    public ResourceRuntimeSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    /// <summary>Creates a fully initialized clip on the audio owner.</summary>
    /// <param name="data">The PCM whose storage remains alive until completion.</param>
    /// <param name="context">The construction recording this runtime version.</param>
    /// <param name="cancellationToken">Cancels before command execution.</param>
    /// <returns>The actual backend clip, never a placeholder.</returns>
    public ValueTask<IAudioClip> CreateClipAsync(AudioPcmData data, ResourceBuildContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        Track(context);
        return _audio.State.CreateClip(data.Format, data.Samples, cancellationToken);
    }

    /// <summary>Creates a fully initialized voice on the audio owner.</summary>
    /// <param name="clip">The retained backend clip.</param>
    /// <param name="context">The construction recording this runtime version.</param>
    /// <param name="cancellationToken">Cancels before command execution.</param>
    /// <returns>The actual stopped backend voice.</returns>
    public ValueTask<IAudioVoice> CreateVoiceAsync(IAudioClip clip, ResourceBuildContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(clip);
        Track(context);
        return new AudioClip(clip).CreateVoice(AudioBus.Effects, cancellationToken);
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
        SetSnapshot(new(1, true, 0));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask StopAsync(ModuleContext context)
    {
        SetSnapshot(new(2, false, 1));
        return ValueTask.CompletedTask;
    }

    private void Track(ResourceBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.TrackRuntimeDependency(Id, Snapshot.Version, providerIdentity: this);
    }

    private void SetSnapshot(ResourceRuntimeSnapshot snapshot)
    {
        lock (_gate)
        {
            _snapshot = snapshot;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
