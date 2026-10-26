using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>A parameterless module with generated audio commands and owner-driven updates.</summary>
/// <param name="factory">Factory called on the configured owner.</param>
public sealed class AudioModule(IAudioDeviceFactory factory) : IExecutionModule
{
    private readonly AudioDevice _state = new(new AudioDeviceState(factory));
    private AudioDevice.BeginCreateWork _begin;

    private AudioDevice.CompleteCreateWork _initialize;
    private AudioDevice.BeginCloseWork _beginClose;
    private AudioDevice.CloseWork _close;
    private OwnerKey _owner;
    private UpdateKey _update;

    /// <summary>Initializes a new instance of the <see cref="AudioModule"/> class using OpenAL.</summary>
    public AudioModule()
        : this(new OpenALDeviceFactory())
    {
    }

    /// <inheritdoc/>
    public string Name => "Audio";

    /// <summary>Gets the generated command declarations and owner diagnostics.</summary>
    public AudioDevice State => _state;

    /// <summary>Gets the stable service managing replaceable audio generations.</summary>
    public IAudioDeviceService Devices => _state.Devices;

    /// <summary>Gets the host phase in host placement mode.</summary>
    public PhaseKey HostPhase { get; private set; }

    /// <inheritdoc/>
    public void Declare(ModuleBuilder builder)
    {
        _owner = builder.Owner("Device", OwnerRequirement.FixedThread);
        OrderGroupKey group = builder.OrderGroup("Control", _owner);
        WorkOptions Options(WorkAdmission admission) => new() { Target = WorkTarget.On(_owner), OrderGroup = group, Admission = admission };
        _begin = AudioDevice.DeclareBeginCreate(builder, Options(WorkAdmission.Startup));
        _initialize = AudioDevice.DeclareCompleteCreate(builder, Options(WorkAdmission.Cleanup));
        _beginClose = AudioDevice.DeclareBeginClose(builder, Options(WorkAdmission.Cleanup));
        AudioDevice.DeclareBeginRecovery(builder, Options(WorkAdmission.Normal));
        AudioDevice.DeclareAcquireLease(builder, Options(WorkAdmission.Normal));
        AudioDevice.DeclareRetire(builder, Options(WorkAdmission.Cleanup));
        AudioDevice.DeclareCreateClipForLease(builder, Options(WorkAdmission.Normal));
        AudioDevice.DeclareCreateClipWithLease(builder, Options(WorkAdmission.Normal));
        _close = AudioDevice.DeclareClose(builder, Options(WorkAdmission.Cleanup));
        AudioDevice.DeclareCreateClip(builder, Options(WorkAdmission.Normal));
        AudioClip.DeclareCreateVoice(builder, Options(WorkAdmission.Normal));
        AudioClip.DeclareDestroyClip(builder, Options(WorkAdmission.Cleanup));
        AudioDevice.DeclareSetBusGain(builder, Options(WorkAdmission.Normal));
        AudioDevice.DeclareRefresh(builder, Options(WorkAdmission.Normal));
        AudioVoice.DeclareDestroy(builder, Options(WorkAdmission.Cleanup));
        AudioVoice.DeclarePause(builder, Options(WorkAdmission.Normal));
        AudioVoice.DeclarePlay(builder, Options(WorkAdmission.Normal));
        AudioVoice.DeclareSeek(builder, Options(WorkAdmission.Normal));
        AudioVoice.DeclareSetGain(builder, Options(WorkAdmission.Normal));
        AudioVoice.DeclareSetLoop(builder, Options(WorkAdmission.Normal));
        AudioVoice.DeclareStop(builder, Options(WorkAdmission.Normal));
        _update = _state.DeclareUpdate(builder, _owner).Key;
    }

    /// <inheritdoc/>
    public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
    {
        if (placement == OwnerPlacement.Host)
        {
            HostPhase = builder.HostPhase("Control");
            builder.BindHost(_owner, HostPhase);
            builder.DriveOnHost(_update, HostPhase);
        }
        else
        {
            ThreadKey thread = builder.DedicatedThread("Control");
            builder.BindDedicated(_owner, thread);
            builder.DrivePeriodic(_update, new ThreadLoopOptions(TimeSpan.FromMilliseconds(10), 64, TimeSpan.FromMilliseconds(2)));
        }
    }

    /// <inheritdoc/>
    public async ValueTask StartAsync(ModuleContext context, CancellationToken token)
    {
        Task<Lumyte.Audio.Abstractions.IAudioDevice> pending = await _begin.ExecuteAsync(context, _state, token).ConfigureAwait(false);
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch
        {
            // The owner completes the state transition and reports the factory failure.
        }

        await _initialize.ExecuteAsync(context, _state).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask StopAsync(ModuleContext context)
    {
        await _beginClose.ExecuteAsync(context, _state).ConfigureAwait(false);
        Task<Lumyte.Audio.Abstractions.IAudioDevice>? pending = _state.PendingCreation;
        if (pending is not null)
        {
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch
            {
                // Failed creation owns no device; successful candidates are reclaimed by Close.
            }
        }

        try
        {
            await _state.RecoveryCompletion.ConfigureAwait(false);
        }
        catch
        {
            // Shutdown owns the pending factory result even if recovery admission was closed.
        }

        await _close.ExecuteAsync(context, _state).ConfigureAwait(false);
    }
}
