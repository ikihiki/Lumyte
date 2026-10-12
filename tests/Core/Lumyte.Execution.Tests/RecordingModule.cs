using System.Collections.Concurrent;

namespace Lumyte.Execution.Tests;

internal sealed class RecordingModule : IExecutionModule
{
    private WorkKey<int, int> _initialize;
    private WorkKey<int, int> _close;
    private OwnerKey _owner;
    private UpdateKey _update;
    private GateKey _gate;

    /// <inheritdoc/>
    public string Name => "Recording";

    internal CommandPort<int, int> Port { get; } = new();

    internal WorkKey<int, int> InitializeKey => _initialize;

    internal CommandPort<int, int> IndependentPort { get; } = new();

    internal CommandPort<long, long> OtherPort { get; } = new();

    internal CommandPort<int, int> StartupPort { get; } = new();

    internal ConcurrentQueue<int> Executed { get; } = new();

    internal GateControl? Gate { get; private set; }

    internal PhaseKey Phase { get; private set; }

    internal bool OnHost { get; private set; }

    internal bool Gated { get; init; }

    internal bool FailCleanupOnce { get; set; }

    internal int DeclareCalls { get; private set; }

    internal int ConfigureCalls { get; private set; }

    internal int OwnerThread { get; private set; }

    internal int CleanupThread { get; private set; }

    internal int CleanupCalls { get; private set; }

    internal int Updates { get; set; }

    internal Action<int>? BeforeExecute { get; set; }

    internal ModuleContext? SavedContext { get; private set; }

    /// <inheritdoc/>
    public void Declare(ModuleBuilder builder)
    {
        DeclareCalls++;
        _owner = builder.Owner("Owner", OwnerRequirement.FixedThread);
        OrderGroupKey group = builder.OrderGroup("FIFO", _owner);
        _gate = builder.Gate("Ready");
        WorkOptions Options(WorkAdmission admission, bool gated = false) => new()
        {
            Target = WorkTarget.On(_owner),
            OrderGroup = group,
            Admission = admission,
            RequiredGates = gated ? new GateKey[] { _gate } : default,
        };
        _initialize = builder.Command<int, int, Handler>("Initialize", new Handler(this, 1), Options(WorkAdmission.Startup), StartupPort);
        _close = builder.Command<int, int, Handler>("Close", new Handler(this, 2), Options(WorkAdmission.Cleanup));
        builder.Command<int, int, Handler>("Control", new Handler(this, 0), Options(WorkAdmission.Normal, Gated), Port);
        builder.Command<long, long, OtherHandler>("OtherControl", new OtherHandler(this), Options(WorkAdmission.Normal, Gated), OtherPort);
        OrderGroupKey independent = builder.OrderGroup("Independent", _owner);
        builder.Command<int, int, Handler>("IndependentControl", new Handler(this, 0), new WorkOptions { Target = WorkTarget.On(_owner), OrderGroup = independent }, IndependentPort);
        _update = builder.Update("Update", _owner, new UpdateHandler(this));
    }

    /// <inheritdoc/>
    public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
    {
        ConfigureCalls++;
        OnHost = placement == OwnerPlacement.Host;
        if (OnHost)
        {
            Phase = builder.HostPhase("Phase");
            builder.BindHost(_owner, Phase);
            builder.DriveOnHost(_update, Phase);
        }
        else
        {
            ThreadKey thread = builder.DedicatedThread("Thread");
            builder.BindDedicated(_owner, thread);
            builder.DrivePeriodic(_update, new ThreadLoopOptions(TimeSpan.FromMilliseconds(5), 64, TimeSpan.FromMilliseconds(2)));
        }
    }

    /// <inheritdoc/>
    public async ValueTask StartAsync(ModuleContext context, CancellationToken token)
    {
        SavedContext = context;
        Gate = context.GetGate(_gate);
        await context.ExecuteAsync(_initialize, 0, token).ConfigureAwait(false);
        if (!Gated)
        {
            Gate.Open();
        }
    }

    /// <inheritdoc/>
    public async ValueTask StopAsync(ModuleContext context) => await context.ExecuteAsync(_close, 0).ConfigureAwait(false);

    private readonly struct Handler(RecordingModule module, int kind) : ICommandHandler<int, int>
    {
        public int Execute(in int command, in WorkContext context)
        {
            context.VerifyAccess();
            if (kind == 1)
            {
                module.OwnerThread = Environment.CurrentManagedThreadId;
            }
            else if (kind == 2)
            {
                module.CleanupThread = Environment.CurrentManagedThreadId;
                module.CleanupCalls++;
                if (module.FailCleanupOnce && module.CleanupCalls == 1)
                {
                    throw new InvalidOperationException("Cleanup failed once.");
                }
            }
            else
            {
                module.BeforeExecute?.Invoke(command);
                if (command == 999)
                {
                    throw new InvalidOperationException("Actual handler failure.");
                }

                if (command == -999)
                {
                    throw new OperationCanceledException("Handler domain failure.");
                }

                if (command == 1000)
                {
                    module.Port.ExecuteAsync(0);
                }

                module.Executed.Enqueue(command);
            }

            return command * 2;
        }
    }

    private readonly struct OtherHandler(RecordingModule module) : ICommandHandler<long, long>
    {
        public long Execute(in long command, in WorkContext context)
        {
            context.VerifyAccess();
            module.Executed.Enqueue((int)command);
            return command * 3;
        }
    }

    private readonly struct UpdateHandler(RecordingModule module) : IUpdateHandler
    {
        public void Update(in UpdateTick tick, in WorkContext context)
        {
            context.VerifyAccess();
            module.Updates++;
        }
    }
}
