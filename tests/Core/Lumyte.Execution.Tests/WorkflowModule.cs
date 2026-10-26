namespace Lumyte.Execution.Tests;

internal sealed class WorkflowModule : IExecutionModule, IWorkflowHandler<int, int>, ICommandHandler<int, int>
{
    private OwnerKey _owner;

    public string Name => "Workflows";

    internal WorkflowPort<int, int> Port { get; } = new();

    internal CommandPort<int, int> Cpu { get; } = new();

    internal CommandPort<int, int> Owner { get; } = new();

    internal Func<int, CancellationToken, ValueTask<int>> Handler { get; set; } = (value, _) => ValueTask.FromResult(value);

    internal Action? BeforeCommand { get; set; }

    internal PhaseKey Phase { get; private set; }

    internal bool OnHost { get; private set; }

    internal int Commands { get; private set; }

    internal int OwnerThread { get; private set; }

    public void Declare(ModuleBuilder builder)
    {
        builder.Workflow<int, int, WorkflowModule>("Build", this, Port);
        builder.Command<int, int, WorkflowModule>("Cpu", this, new WorkOptions { Target = WorkTarget.Cpu }, Cpu);
        _owner = builder.Owner("Owner", OwnerRequirement.FixedThread);
        OrderGroupKey group = builder.OrderGroup("Order", _owner);
        builder.Command<int, int, WorkflowModule>("OwnerCommand", this, new WorkOptions { Target = WorkTarget.On(_owner), OrderGroup = group }, Owner);
    }

    public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
    {
        OnHost = placement == OwnerPlacement.Host;
        if (OnHost)
        {
            Phase = builder.HostPhase("Phase");
            builder.BindHost(_owner, Phase);
        }
        else
        {
            builder.BindDedicated(_owner, builder.DedicatedThread("OwnerThread"));
        }
    }

    public ValueTask StartAsync(ModuleContext context, CancellationToken token) => ValueTask.CompletedTask;

    public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;

    public ValueTask<int> ExecuteAsync(int command, CancellationToken cancellationToken) => Handler(command, cancellationToken);

    public int Execute(in int command, in WorkContext context)
    {
        context.VerifyAccess();
        BeforeCommand?.Invoke();
        Commands++;
        OwnerThread = Environment.CurrentManagedThreadId;
        return command * 2;
    }
}
