using Lumyte.Execution;

namespace Lumyte.UI.Application.WebView.Cef;

// Owned by the participant, or by a directly constructed session in isolation tests.
// Never registered with the UI's shared resource execution runtime.
internal sealed class CefExecutionScope
{
    private readonly object _gate = new();
    private readonly SessionModule _module;
    private readonly ExecutionRuntime _runtime;
    private Task? _started;
    private bool _stopping;

    internal CefExecutionScope(Func<ValueTask>? stop = null)
    {
        _module = new(stop);
        _runtime = new([_module]);
        _runtime.PrepareOnCurrentHost(OwnerPlacement.Dedicated, CpuExecutionMode.ThreadPool, 1, new ExecutionOptions
        {
            MaxInFlightRuns = 1,
            MaxCommands = 1,
            MaxPayloadBytes = 1024,
        });
    }

    internal Task StartAsync()
    {
        lock (_gate)
        {
            return _stopping ? Task.FromException(new InvalidOperationException("cef-execution-stopping")) : _started ??= _runtime.StartAsync().AsTask();
        }
    }

    internal async Task ExecuteAsync(Func<Task> run, CancellationToken cancellationToken)
    {
        // A session closed before startup must never start the runtime or launch native code.
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await StartAsync();
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // Observe exactly once, including the result retained during asynchronous I/O.
        // The session token controls I/O directly; runtime shutdown alone cannot abort it.
        await _module.Session.ExecuteAsync(new(run));
    }

    internal async Task StopAsync()
    {
        Task? started;
        lock (_gate)
        {
            _stopping = true;
            started = _started;
        }

        if (started is not null)
        {
            await started;
        }

        // Caller first cancels and joins every session root. No pending-work deadlock.
        await _runtime.StopAsync();
    }

    private readonly record struct SessionRequest(Func<Task> Run);

    private readonly struct SessionHandler : IWorkflowHandler<SessionRequest, Unit>
    {
        public async ValueTask<Unit> ExecuteAsync(SessionRequest command, CancellationToken cancellationToken)
        {
            await command.Run();
            return default;
        }
    }

    private sealed class SessionModule(Func<ValueTask>? stop) : IExecutionModule
    {
        public string Name => "CefSessions";

        internal WorkflowPort<SessionRequest, Unit> Session { get; } = new();

        public void Declare(ModuleBuilder builder) => builder.Workflow<SessionRequest, Unit, SessionHandler>("Session", default, Session);

        public void Configure(ExecutionPlanBuilder builder, OwnerPlacement placement)
        {
        }

        public ValueTask StartAsync(ModuleContext context, CancellationToken token) => ValueTask.CompletedTask;

        public ValueTask StopAsync(ModuleContext context) => stop?.Invoke() ?? ValueTask.CompletedTask;
    }
}
