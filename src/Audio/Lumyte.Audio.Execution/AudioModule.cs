using Lumyte.Execution;

namespace Lumyte.Audio.Execution;

/// <summary>A parameterless module with generated audio commands and owner-driven updates.</summary>
/// <param name="factory">Factory called on the configured owner.</param>
public sealed class AudioModule(IAudioDeviceFactory factory) : IExecutionModule
{
    private readonly AudioDevice _state = new(factory ?? throw new ArgumentNullException(nameof(factory)));
    private AudioDevice.BeginCreateWork _begin;

    private AudioDevice.AttachWork _initialize;
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

    /// <summary>Gets the host phase in host placement mode.</summary>
    public PhaseKey HostPhase { get; private set; }

    /// <inheritdoc/>
    public void Declare(ModuleBuilder builder)
    {
        _owner = builder.Owner("Device", OwnerRequirement.FixedThread);
        OrderGroupKey group = builder.OrderGroup("Control", _owner);
        WorkOptions Options(WorkAdmission admission) => new() { Target = WorkTarget.On(_owner), OrderGroup = group, Admission = admission };
        _begin = _state.DeclareBeginCreate(builder, Options(WorkAdmission.Startup));
        _initialize = _state.DeclareAttach(builder, Options(WorkAdmission.Startup));
        _close = _state.DeclareClose(builder, Options(WorkAdmission.Cleanup));
        _state.DeclareCreateClip(builder, Options(WorkAdmission.Normal));
        AudioClip.DeclareCommands(builder, Options(WorkAdmission.Normal), Options(WorkAdmission.Cleanup));
        _state.DeclareSetBusGain(builder, Options(WorkAdmission.Normal));
        _state.DeclareRefresh(builder, Options(WorkAdmission.Normal));
        AudioVoice.DeclareCommands(builder, Options(WorkAdmission.Cleanup), Options(WorkAdmission.Normal), Options(WorkAdmission.Normal), Options(WorkAdmission.Normal), Options(WorkAdmission.Normal), Options(WorkAdmission.Normal));
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
        Task<Lumyte.Audio.Abstractions.IAudioDevice> pending = await _begin.ExecuteAsync(context, token).ConfigureAwait(false);
        Lumyte.Audio.Abstractions.IAudioDevice device = await pending.ConfigureAwait(false);
        await _initialize.ExecuteAsync(context, device).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask StopAsync(ModuleContext context) => await _close.ExecuteAsync(context).ConfigureAwait(false);
}
