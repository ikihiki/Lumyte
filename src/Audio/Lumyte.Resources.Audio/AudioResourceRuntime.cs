using System.Runtime.ExceptionServices;
using Lumyte.Audio.Abstractions;
using Lumyte.Audio.Execution;
using Lumyte.Execution;

namespace Lumyte.Resources.Audio;

/// <summary>Provides the shared executor, tracked device environment and owner operations for audio stages.</summary>
/// <remarks>The audio module owns physical device generations. This module declares its required startup dependency.</remarks>
public sealed class AudioResourceRuntime : IResourceRuntime, IExecutionModule, IResourceRuntimeDependency
{
    private readonly IAudioDeviceService _devices;

    /// <summary>Initializes a new instance of the <see cref="AudioResourceRuntime"/> class.</summary>
    /// <param name="devices">The stable service provided by the registered audio module.</param>
    /// <param name="defaultRuntime">The shared environment supplying stage execution and continuations.</param>
    public AudioResourceRuntime(IAudioDeviceService devices, IResourceRuntime defaultRuntime)
    {
        _devices = devices;
        Executor = defaultRuntime.Executor;
        Dependencies = [this];
    }

    /// <inheritdoc/>
    public event EventHandler? Changed
    {
        add => _devices.StateChanged += value;
        remove => _devices.StateChanged -= value;
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
    public object Identity => _devices;

    /// <inheritdoc/>
    public ResourceRuntimeSnapshot Snapshot
    {
        get
        {
            AudioDeviceSnapshot state = _devices.State;
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

        return context.UseAudioAsync(_devices, Id, lifetime, cancellationToken);
    }

    /// <summary>Creates a fully initialized clip on the audio owner.</summary>
    /// <param name="data">The PCM whose storage remains alive until completion.</param>
    /// <param name="context">The construction recording this runtime version.</param>
    /// <param name="cancellationToken">Cancels before command execution.</param>
    /// <returns>A sender wrapping the fully created backend clip, never a placeholder.</returns>
    public async ValueTask<AudioClip> CreateClipAsync(AudioPcmData data, ResourceBuildContext context, CancellationToken cancellationToken = default)
    {
        AudioDeviceLease lease = await _devices.AcquireAsync(cancellationToken);
        AudioDevice device;
        AudioClip clip;
        Exception? validationError;
        try
        {
            device = lease.Device;
            (clip, validationError) = await device.CreateClipWithValidationAsync(data.Format, data.Samples, cancellationToken);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

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

        // Register native cleanup before observing validation failure: a failed query must not
        // lose the newly created clip or return its generation before cleanup succeeds.
        if (validationError is not null)
        {
            ExceptionDispatchInfo.Throw(validationError);
        }

        if (!lease.IsUsable)
        {
            throw new AudioDeviceUnavailableException();
        }

        context.TrackRuntimeDependency(Id, lease.State.Version, ResourceDependencyLifetime.Resident, _devices);
        pending.Transfer();
        return clip;
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

    private sealed class PendingCreation(AudioDeviceLease lease, AudioClip resource)
    {
        private AudioClip? _resource = resource;

        internal void Transfer() => _resource = null;

        internal async ValueTask DisposeAsync()
        {
            if (_resource is AudioClip clip)
            {
                await clip.DestroyClipAsync();
            }

            _resource = null;
            lease.Dispose();
        }
    }
}
