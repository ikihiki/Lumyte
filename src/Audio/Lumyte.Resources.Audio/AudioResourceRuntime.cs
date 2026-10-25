using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;
using Lumyte.Execution;

namespace Lumyte.Resources.Audio;

/// <summary>Provides the shared executor, tracked device environment and owner operations for audio stages.</summary>
/// <remarks>The audio module owns physical device generations. This module declares its required startup dependency.</remarks>
public sealed class AudioResourceRuntime : IResourceRuntime, IExecutionModule, IResourceRuntimeDependency
{
    private readonly AudioModule _audio;

    /// <summary>Initializes a new instance of the <see cref="AudioResourceRuntime"/> class.</summary>
    /// <param name="audio">The same audio module collected by the execution runtime.</param>
    /// <param name="defaultRuntime">The shared environment supplying stage execution and continuations.</param>
    public AudioResourceRuntime(AudioModule audio, IResourceRuntime defaultRuntime)
    {
        _audio = audio;
        Executor = defaultRuntime.Executor;
        Dependencies = [this];
    }

    /// <inheritdoc/>
    public event EventHandler? Changed
    {
        add => _audio.Devices.StateChanged += value;
        remove => _audio.Devices.StateChanged -= value;
    }

    /// <inheritdoc/>
    public IResourceExecutor Executor { get; }

    /// <inheritdoc/>
    public IReadOnlyList<IResourceRuntimeDependency> Dependencies { get; }

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
        (AudioDeviceLease lease, IAudioClip clip) = await _audio.State.CreateClipWithLease(data.Format, data.Samples, cancellationToken);
        var pending = new PendingCreation(lease, clip);
        try
        {
            context.RegisterCleanup(pending.DisposeAsync, ResourceDependencyLifetime.Resident);
        }
        catch (Exception registrationError)
        {
            try
            {
                await pending.DisposeAsync();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException(registrationError, cleanupError);
            }

            throw;
        }

        context.TrackRuntimeDependency(Id, lease.State.Version, ResourceDependencyLifetime.Resident, _audio.Devices);
        pending.Transfer();
        return clip;
    }

    /// <summary>Creates a fully initialized voice on the audio owner.</summary>
    /// <param name="clip">The retained backend clip.</param>
    /// <param name="context">The construction recording this runtime version.</param>
    /// <param name="cancellationToken">Cancels before command execution.</param>
    /// <returns>The actual stopped backend voice.</returns>
    public async ValueTask<IAudioVoice> CreateVoiceAsync(IAudioClip clip, ResourceBuildContext context, CancellationToken cancellationToken = default)
    {
        (AudioDeviceLease lease, IAudioVoice voice) = await _audio.State.CreateVoiceWithLease(clip, AudioBus.Effects, cancellationToken);
        var pending = new PendingCreation(lease, voice);
        try
        {
            context.RegisterCleanup(pending.DisposeAsync, ResourceDependencyLifetime.Resident);
        }
        catch (Exception registrationError)
        {
            try
            {
                await pending.DisposeAsync();
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException(registrationError, cleanupError);
            }

            throw;
        }

        context.TrackRuntimeDependency(Id, lease.State.Version, ResourceDependencyLifetime.Resident, _audio.Devices);
        pending.Transfer();
        return voice;
    }

    /// <summary>Destroys a clip through its original backend instance on the audio owner.</summary>
    /// <param name="clip">The original clip retained by the manager.</param>
    /// <returns>The actual native cleanup completion.</returns>
    public async ValueTask DestroyClipAsync(IAudioClip clip) => await new AudioClip(clip).DestroyClip();

    /// <summary>Destroys a voice through its original backend instance on the audio owner.</summary>
    /// <param name="voice">The original voice retained by the manager.</param>
    /// <returns>The actual native cleanup completion.</returns>
    public async ValueTask DestroyVoiceAsync(IAudioVoice voice) => await new AudioVoice(voice).Destroy();

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

    private sealed class PendingCreation(AudioDeviceLease lease, IDisposable resource)
    {
        private IDisposable? _resource = resource;

        internal void Transfer() => _resource = null;

        internal async ValueTask DisposeAsync()
        {
            if (_resource is IAudioClip clip)
            {
                await new AudioClip(clip).DestroyClip();
            }
            else if (_resource is IAudioVoice voice)
            {
                await new AudioVoice(voice).Destroy();
            }

            _resource = null;
            lease.Dispose();
        }
    }
}
