namespace Lumyte.Execution.Tests;

internal sealed class GraphModule : IExecutionModule
{
    private OwnerKey _owner;
    private GateKey _gate;
    private int _active;
    private int _peak;

    /// <inheritdoc/>
    public string Name => "Graph";

    internal WorkKey<GraphInput, Unit> Prepare { get; private set; }

    internal WorkKey<GraphInput, Unit> Apply { get; private set; }

    internal RangeWorkKey<GraphInput> Range { get; private set; }

    internal PhaseKey Phase { get; private set; }

    internal GateControl? Gate { get; private set; }

    internal bool GatedCpu { get; init; }

    internal int Peak => Volatile.Read(ref _peak);

    /// <inheritdoc/>
    public void Declare(ModuleBuilder builder)
    {
        _owner = builder.Owner("Owner", OwnerRequirement.FixedThread);
        OrderGroupKey group = builder.OrderGroup("Group", _owner);
        _gate = builder.Gate("Ready");
        Prepare = builder.Command<GraphInput, Unit, Handler>("Prepare", new Handler(false), new WorkOptions { Target = WorkTarget.Cpu, RequiredGates = GatedCpu ? new GateKey[] { _gate } : default });
        Apply = builder.Command<GraphInput, Unit, Handler>("Apply", new Handler(true), new WorkOptions { Target = WorkTarget.On(_owner), OrderGroup = group, RequiredGates = new GateKey[] { _gate } });
        Range = builder.Range<GraphInput, RangeHandler>("Range", new RangeHandler(this));
    }

    /// <inheritdoc/>
    public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
    {
        if (placement == OwnerPlacement.Host)
        {
            Phase = builder.HostPhase("Phase");
            builder.BindHost(_owner, Phase);
        }
        else
        {
            builder.BindDedicated(_owner, builder.DedicatedThread("Owner"));
        }
    }

    /// <inheritdoc/>
    public ValueTask StartAsync(ModuleContext context, CancellationToken token)
    {
        Gate = context.GetGate(_gate);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;

    private readonly struct Handler(bool owner) : ICommandHandler<GraphInput, Unit>
    {
        public Unit Execute(in GraphInput command, in WorkContext context)
        {
            context.VerifyAccess();
            if (owner)
            {
                command.Data.Total = command.Data.Values.Sum();
                command.Data.ApplyThread = Environment.CurrentManagedThreadId;
                command.Data.Events.Enqueue("apply");
            }
            else
            {
                if (command.Data.FailPrepare)
                {
                    throw new InvalidOperationException("Prepare failed.");
                }

                command.Data.Events.Enqueue("prepare");
            }

            return default;
        }
    }

    private readonly struct RangeHandler(GraphModule module) : IRangeWorkHandler<GraphInput>
    {
        public int GetLength(in GraphInput context) => context.Data.Values.Length;

        public void Execute(in GraphInput context, int first, int endExclusive)
        {
            int active = Interlocked.Increment(ref module._active);
            int old;
            do
            {
                old = Volatile.Read(ref module._peak);
                if (old >= active)
                {
                    break;
                }
            }
            while (Interlocked.CompareExchange(ref module._peak, active, old) != old);
            try
            {
                if (context.Data.SlowRange)
                {
                    Thread.Sleep(5);
                }

                for (int i = first; i < endExclusive; i++)
                {
                    context.Data.Values[i] = i + 1;
                }

                context.Data.Events.Enqueue("range");
            }
            finally
            {
                Interlocked.Decrement(ref module._active);
            }
        }
    }
}
