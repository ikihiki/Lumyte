namespace Lumyte.Execution.Sample;

/// <summary>A parameterless module with generated audio commands and owner-driven updates.</summary>
public sealed class DemoModule : IExecutionModule
{
    private readonly DemoState _state = new(new DemoBackend());
    private DemoState.InitializeWork _initialize;
    private DemoState.CloseWork _close;
    private OwnerKey _owner;
    private UpdateKey _update;

    /// <inheritdoc/>
    public string Name => "Demo";

    /// <summary>Gets the state used for thread and update diagnostics.</summary>
    public DemoState State => _state;

    /// <summary>Gets the host phase in host placement mode.</summary>
    public PhaseKey HostPhase { get; private set; }

    /// <inheritdoc/>
    public void Declare(ModuleBuilder builder)
    {
        _owner = builder.Owner("Device", OwnerRequirement.FixedThread);
        OrderGroupKey group = builder.OrderGroup("Control", _owner);
        WorkOptions Options(WorkAdmission admission) => new() { Target = WorkTarget.On(_owner), OrderGroup = group, Admission = admission };
        _initialize = DemoState.DeclareInitialize(builder, Options(WorkAdmission.Startup));
        _close = DemoState.DeclareClose(builder, Options(WorkAdmission.Cleanup));
        DemoState.DeclareCreateVoice(builder, Options(WorkAdmission.Normal));
        DemoVoice.DeclareDestroy(builder, Options(WorkAdmission.Cleanup));
        DemoVoice.DeclarePlay(builder, Options(WorkAdmission.Normal));
        DemoVoice.DeclareStop(builder, Options(WorkAdmission.Normal));
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
    public async ValueTask StartAsync(ModuleContext context, CancellationToken token) => await _initialize.ExecuteAsync(context, _state, token).ConfigureAwait(false);

    /// <inheritdoc/>
    public async ValueTask StopAsync(ModuleContext context) => await _close.ExecuteAsync(context, _state).ConfigureAwait(false);
}
