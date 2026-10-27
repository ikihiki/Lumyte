using System.Runtime.ExceptionServices;
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

    /// <summary>Acquires and records the current device generation for this build.</summary>
    /// <param name="context">The build using this selected runtime.</param>
    /// <param name="lifetime">Whether the device is retained for construction or for the completed output.</param>
    /// <param name="cancellationToken">Cancels before acquisition executes.</param>
    /// <returns>A borrowed lease returned by the manager after the corresponding native cleanup.</returns>
    public ValueTask<AudioDeviceLease> AcquireAsync(ResourceBuildContext context, ResourceDependencyLifetime lifetime = ResourceDependencyLifetime.Resident, CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(context.Runtime, this))
        {
            throw new InvalidOperationException("Use the audio runtime selected for this stage.");
        }

        return context.UseAudioAsync(_audio.Devices, Id, lifetime, cancellationToken);
    }

    /// <summary>Creates a fully initialized clip on the audio owner.</summary>
    /// <param name="data">The PCM whose storage remains alive until completion.</param>
    /// <param name="context">The construction recording this runtime version.</param>
    /// <param name="cancellationToken">Cancels before command execution.</param>
    /// <returns>A sender wrapping the fully created backend clip, never a placeholder.</returns>
    public async ValueTask<AudioClip> CreateClipAsync(AudioPcmData data, ResourceBuildContext context, CancellationToken cancellationToken = default)
    {
        (AudioDeviceLease lease, IAudioClip clip, Exception? validationError) = await _audio.Commands.CreateClipWithLeaseAsync(data.Format, data.Samples, cancellationToken);
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

        if (validationError is not null)
        {
            ExceptionDispatchInfo.Throw(validationError);
        }

        context.TrackRuntimeDependency(Id, lease.State.Version, ResourceDependencyLifetime.Resident, _audio.Devices);
        pending.Transfer();
        return new(clip);
    }

    /// <summary>Destroys a clip through its original backend instance on the audio owner.</summary>
    /// <param name="clip">The original clip retained by the manager.</param>
    /// <returns>The actual native cleanup completion.</returns>
    public async ValueTask DestroyClipAsync(AudioClip clip) => await clip.DestroyClipAsync();

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

    private sealed class PendingCreation(AudioDeviceLease lease, IAudioClip resource)
    {
        private IAudioClip? _resource = resource;

        internal void Transfer() => _resource = null;

        internal async ValueTask DisposeAsync()
        {
            if (_resource is IAudioClip clip)
            {
                await new AudioClip(clip).DestroyClipAsync();
            }

            _resource = null;
            lease.Dispose();
        }
    }
}
