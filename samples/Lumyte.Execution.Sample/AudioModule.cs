namespace Lumyte.Execution.Sample;

/// <summary>A parameterless module with generated audio commands and owner-driven updates.</summary>
public sealed class AudioModule : IExecutionModule
{
    private readonly AudioState _state = new();
    private AudioState.InitializeWork _initialize;
    private AudioState.CloseWork _close;
    private OwnerKey _owner;
    private UpdateKey _update;

    /// <inheritdoc/>
    public string Name => "Audio";

    /// <summary>Gets the state used for thread and update diagnostics.</summary>
    public AudioState State => _state;

    /// <summary>Gets the host phase in host placement mode.</summary>
    public PhaseKey HostPhase { get; private set; }

    /// <inheritdoc/>
    public void Declare(ModuleBuilder builder)
    {
        _owner = builder.Owner("Device", OwnerRequirement.FixedThread);
        OrderGroupKey group = builder.OrderGroup("Control", _owner);
        WorkOptions Options(WorkAdmission admission) => new() { Target = WorkTarget.On(_owner), OrderGroup = group, Admission = admission };
        _initialize = _state.DeclareInitialize(builder, Options(WorkAdmission.Startup));
        _close = _state.DeclareClose(builder, Options(WorkAdmission.Cleanup));
        _state.DeclareCreateVoice(builder, Options(WorkAdmission.Normal));
        _state.DeclareVoice(builder, Options(WorkAdmission.Cleanup), Options(WorkAdmission.Normal), Options(WorkAdmission.Normal));
        _update = _state.DeclareUpdateControl(builder, _owner).Key;
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
    public async ValueTask StartAsync(ModuleContext context, CancellationToken token) => await _initialize.ExecuteAsync(context, token).ConfigureAwait(false);

    /// <inheritdoc/>
    public async ValueTask StopAsync(ModuleContext context) => await _close.ExecuteAsync(context).ConfigureAwait(false);
}
