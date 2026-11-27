using Lumyte.Execution;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares graphics execution module.</summary>
public sealed class GraphicsExecutionModule : IExecutionModule
{
    private readonly CommandPort<OwnerAction, bool> _work = new();
    private readonly CommandPort<OwnerAction, bool> _cleanup = new();
    private OwnerKey _owner;
    private WorkContext? _activeContext;

    /// <summary>Gets name.</summary>
    public string Name => "GraphicsPlanning";

    /// <summary>Gets host phase.</summary>
    public PhaseKey HostPhase { get; private set; }

    /// <summary>Declares declare.</summary>
    /// <param name = "builder">The builder.</param>
    public void Declare(ModuleBuilder builder)
    {
        _owner = builder.Owner("NativeOwner", OwnerRequirement.FixedThread);
        OrderGroupKey group = builder.OrderGroup("NativeOrder", _owner);
        builder.Command<OwnerAction, bool, Handler>("NativeWork", new(this), new() { Target = WorkTarget.On(_owner), OrderGroup = group }, _work);
        builder.Command<OwnerAction, bool, Handler>("NativeCleanup", new(this), new() { Target = WorkTarget.On(_owner), OrderGroup = group, Admission = WorkAdmission.Cleanup }, _cleanup);
    }

    /// <summary>Declares configure.</summary>
    /// <param name = "builder">The builder.</param>
    /// <param name = "placement">The placement.</param>
    public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
    {
        if (placement == OwnerPlacement.Host)
        {
            HostPhase = builder.HostPhase("NativePhase");
            builder.BindHost(_owner, HostPhase);
        }
        else
        {
            builder.BindDedicated(_owner, builder.DedicatedThread("NativeThread"));
        }
    }

    /// <summary>Declares start async.</summary>
    /// <param name = "context">The context.</param>
    /// <param name = "token">The token.</param>
    /// <returns>The declared result.</returns>
    public ValueTask StartAsync(ModuleContext context, CancellationToken token) => ValueTask.CompletedTask;

    /// <summary>Declares stop async.</summary>
    /// <param name = "context">The context.</param>
    /// <returns>The declared result.</returns>
    public ValueTask StopAsync(ModuleContext context) => ValueTask.CompletedTask;

    internal void VerifyAccess()
    {
        if (_activeContext is not { } context)
        {
            throw new InvalidOperationException("Native graphics work requires the registered fixed owner.");
        }

        context.VerifyAccess();
    }

    internal async ValueTask RunAsync(Action action, bool retiring = false, CancellationToken cancellationToken = default) => await (retiring ? _cleanup : _work).ExecuteAsync(new(action), cancellationToken).ConfigureAwait(false);

    private readonly record struct OwnerAction(Action Action);

    private readonly struct Handler(GraphicsExecutionModule module) : ICommandHandler<OwnerAction, bool>
    {
        /// <summary>Declares execute.</summary>
        /// <param name = "command">The command.</param>
        /// <param name = "context">The context.</param>
        /// <returns>The declared result.</returns>
        public bool Execute(in OwnerAction command, in WorkContext context)
        {
            context.VerifyAccess();
            module._activeContext = context;
            try
            {
                command.Action();
                return true;
            }
            finally
            {
                module._activeContext = null;
            }
        }
    }
}
