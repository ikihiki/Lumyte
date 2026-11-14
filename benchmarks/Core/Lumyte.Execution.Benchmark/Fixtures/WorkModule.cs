namespace Lumyte.Execution.Benchmark;

internal sealed class WorkModule(bool gated, bool generated = false) : IExecutionModule, ICommandHandler<int, int>, IWorkflowHandler<int, int>, ICommandHandler<ExecutionBenchmarks.Input, Unit>, IRangeWorkHandler<ExecutionBenchmarks.Input>, IUpdateHandler
{
    private OwnerKey _owner;
    private GateKey _gate;
    private UpdateKey _update;

    public string Name => "Benchmark";

    internal CommandPort<int, int> Port { get; } = new();

    internal WorkflowPort<int, int> Workflow { get; } = new();

    internal WorkKey<ExecutionBenchmarks.Input, Unit> GraphWork { get; private set; }

    internal RangeWorkKey<ExecutionBenchmarks.Input> Range { get; private set; }

    internal GateControl Gate { get; private set; } = null!;

    internal PhaseKey Phase { get; private set; }

    internal bool OnHost { get; private set; }

    internal int Commands { get; private set; }

    internal int Updates { get; private set; }

    internal TaskCompletionSource? Resume { get; set; }

    internal Sender.PlayWork SenderWork { get; private set; }

    public void Declare(ModuleBuilder builder)
    {
        _owner = builder.Owner("Owner", OwnerRequirement.FixedThread);
        OrderGroupKey group = builder.OrderGroup("FIFO", _owner);
        _gate = builder.Gate("Ready");
        builder.Command<int, int, WorkModule>("Command", this, new WorkOptions { Target = WorkTarget.On(_owner), OrderGroup = group, RequiredGates = gated ? new[] { _gate } : default }, Port);
        if (generated)
        {
            SenderWork = Sender.DeclarePlay(builder, new WorkOptions { Target = WorkTarget.On(_owner), OrderGroup = group });
        }

        builder.Workflow<int, int, WorkModule>("Workflow", this, Workflow);
        GraphWork = builder.Command<ExecutionBenchmarks.Input, Unit, WorkModule>("Graph", this, new WorkOptions { Target = WorkTarget.Cpu });
        Range = builder.Range<ExecutionBenchmarks.Input, WorkModule>("Range", this);
        _update = builder.Update("Update", _owner, this);
    }

    public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
    {
        OnHost = placement == OwnerPlacement.Host;
        if (OnHost)
        {
            Phase = builder.HostPhase("Phase");
            builder.BindHost(_owner, Phase);
            builder.DriveOnHost(_update, Phase);
        }
        else
        {
            builder.BindDedicated(_owner, builder.DedicatedThread("Owner"));
            builder.DrivePeriodic(_update, new ThreadLoopOptions(TimeSpan.FromMilliseconds(5), 64, TimeSpan.FromMilliseconds(2)));
        }
    }

    public ValueTask StartAsync(ModuleContext context, CancellationToken token)
    {
        Gate = context.GetGate(_gate);
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;

    public int Execute(in int command, in WorkContext context)
    {
        context.VerifyAccess();
        Commands = (Commands + 1) & 0xffff;
        return command * 2;
    }

    public async ValueTask<int> ExecuteAsync(int command, CancellationToken cancellationToken)
    {
        if (command == 42)
        {
            await Resume!.Task;
            return 42;
        }

        return command < 0 ? await Port.ExecuteAsync(-command, cancellationToken) : command;
    }

    public Unit Execute(in ExecutionBenchmarks.Input command, in WorkContext context)
    {
        context.VerifyAccess();
        command.Values[0]++;
        return default;
    }

    public int GetLength(in ExecutionBenchmarks.Input context) => context.Values.Length;

    public void Execute(in ExecutionBenchmarks.Input context, int first, int endExclusive)
    {
        for (int i = first; i < endExclusive; i++)
        {
            context.Values[i]++;
        }
    }

    public void Update(in UpdateTick tick, in WorkContext context)
    {
        context.VerifyAccess();
        Updates = (Updates + 1) & 0xffff;
    }
}
