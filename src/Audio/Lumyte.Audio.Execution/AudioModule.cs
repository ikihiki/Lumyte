using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>A parameterless module with generated audio commands and owner-driven updates.</summary>
/// <param name="factory">Factory called on the configured owner.</param>
public sealed class AudioModule(IAudioDeviceFactory factory) : IExecutionModule
{
    private readonly AudioDeviceControl _control = new(new AudioDeviceState(factory));
    private AudioDeviceControl.BeginCreateWork _begin;

    private AudioDeviceControl.CompleteCreateWork _initialize;
    private AudioDeviceControl.BeginCloseWork _beginClose;
    private AudioDeviceControl.CloseWork _close;
    private OwnerKey _owner;
    private UpdateKey _update;

    /// <summary>Initializes a new instance of the <see cref="AudioModule"/> class using OpenAL.</summary>
    public AudioModule()
        : this(new OpenALDeviceFactory())
    {
    }

    /// <inheritdoc/>
    public string Name => "Audio";

    /// <summary>Gets the stable service managing replaceable audio generations.</summary>
    public IAudioDeviceService Devices => _control.Devices;

    /// <summary>Gets the host phase in host placement mode.</summary>
    public PhaseKey HostPhase { get; private set; }

    /// <inheritdoc/>
    public void Declare(ModuleBuilder builder)
    {
        _owner = builder.Owner("Device", OwnerRequirement.FixedThread);
        OrderGroupKey group = builder.OrderGroup("Control", _owner);
        WorkOptions Options(WorkAdmission admission) => new() { Target = WorkTarget.On(_owner), OrderGroup = group, Admission = admission };
        _begin = AudioDeviceControl.DeclareBeginCreate(builder, Options(WorkAdmission.Startup));
        _initialize = AudioDeviceControl.DeclareCompleteCreate(builder, Options(WorkAdmission.Cleanup));
        _beginClose = AudioDeviceControl.DeclareBeginClose(builder, Options(WorkAdmission.Cleanup));
        AudioDeviceControl.DeclareBeginRecovery(builder, Options(WorkAdmission.Normal));
        AudioDeviceControl.DeclareAcquireLease(builder, Options(WorkAdmission.Normal));
        AudioDeviceControl.DeclareRetire(builder, Options(WorkAdmission.Cleanup));
        _close = AudioDeviceControl.DeclareClose(builder, Options(WorkAdmission.Cleanup));
        AudioDevice.DeclareCreateClip(builder, Options(WorkAdmission.Normal));
        AudioDevice.DeclareCreateClipWithValidation(builder, Options(WorkAdmission.Normal));
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
        _update = _control.DeclareUpdate(builder, _owner).Key;
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
        Task<Lumyte.Audio.Abstractions.IAudioDevice> pending = await _begin.ExecuteAsync(context, _control, token).ConfigureAwait(false);
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch
        {
            // The owner completes the state transition and reports the factory failure.
        }

        await _initialize.ExecuteAsync(context, _control).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask StopAsync(ModuleContext context)
    {
        await _beginClose.ExecuteAsync(context, _control).ConfigureAwait(false);
        Task<Lumyte.Audio.Abstractions.IAudioDevice>? pending = _control.PendingCreation;
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
            await _control.RecoveryCompletion.ConfigureAwait(false);
        }
        catch
        {
            // Shutdown owns the pending factory result even if recovery admission was closed.
        }

        await _close.ExecuteAsync(context, _control).ConfigureAwait(false);
    }
}
