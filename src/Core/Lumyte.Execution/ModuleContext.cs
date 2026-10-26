using System.Threading.Tasks.Sources;

namespace Lumyte.Execution;

/// <summary>Temporary, module-specific startup or cleanup authority.</summary>
public sealed class ModuleContext
{
    private readonly ModuleDefinition _module;
    private readonly bool _stopping;
    private bool _active = true;

    internal ModuleContext(ModuleDefinition module, bool stopping)
    {
        _module = module;
        _stopping = stopping;
    }

    internal bool IsCleanup => _stopping;

    /// <summary>Attempts an authorized lifecycle command admission.</summary>
    /// <typeparam name="TCommand">The command payload type.</typeparam>
    /// <typeparam name="TResult">The handler return type.</typeparam>
    /// <param name="key">The work.</param>
    /// <param name="command">The input.</param>
    /// <param name="token">Cancellation before execution.</param>
    /// <param name="completion">The single-use result.</param>
    /// <returns>The admission status.</returns>
    public CommandEnqueueStatus TryEnqueue<TCommand, TResult>(WorkKey<TCommand, TResult> key, in TCommand command, CancellationToken token, out IValueTaskSource<TResult>? completion)
        where TCommand : struct
    {
        ExecutionRuntime.VerifyOutsideHandler();
        WorkDefinition<TCommand, TResult> work = key.Definition ?? throw new ArgumentException("Invalid work key.", nameof(key));
        return _module.Runtime.Enqueue(work, in command, token, this, out completion);
    }

    /// <summary>Submits lifecycle work and observes its actual execution result.</summary>
    /// <typeparam name="TCommand">The command payload type.</typeparam>
    /// <typeparam name="TResult">The handler return type.</typeparam>
    /// <param name="key">The work.</param>
    /// <param name="command">The input.</param>
    /// <param name="token">Cancellation before execution.</param>
    /// <returns>The single-use result.</returns>
    public ValueTask<TResult> ExecuteAsync<TCommand, TResult>(WorkKey<TCommand, TResult> key, TCommand command, CancellationToken token = default)
        where TCommand : struct
    {
        CommandEnqueueStatus status = TryEnqueue(key, in command, token, out IValueTaskSource<TResult>? completion);
        return status switch
        {
            CommandEnqueueStatus.Accepted => new ValueTask<TResult>(completion!, 0),
            CommandEnqueueStatus.Canceled => ValueTask.FromCanceled<TResult>(token),
            CommandEnqueueStatus.NotRunning => ValueTask.FromException<TResult>(new InvalidOperationException("Lifecycle admission is closed.")),
            CommandEnqueueStatus.QueueFull => ValueTask.FromException<TResult>(new ExecutionQueueFullException("Lifecycle capacity is exhausted.")),
            _ => throw new InvalidOperationException("Unknown enqueue status."),
        };
    }

    /// <summary>Obtains control of a gate declared by this module.</summary>
    /// <param name="key">The local gate.</param>
    /// <returns>A gate controller.</returns>
    public GateControl GetGate(GateKey key)
    {
        lock (_module.Runtime.Sync)
        {
            if (!_active || key.Definition?.Module != _module)
            {
                throw new InvalidOperationException("Invalid gate or expired lifecycle context.");
            }

            return new GateControl(key.Definition);
        }
    }

    internal void Verify(WorkDefinition work)
    {
        bool local = work.Module == _module;
        bool dependency = _module.Runtime.IsDependency(_module, work.Module);
        if (!_active || work.Module.Runtime != _module.Runtime || (!local && !dependency)
            || (_stopping && (work.Admission != WorkAdmission.Cleanup || work.Module.Stopped))
            || (!_stopping && !local && (!work.Module.Initialized || work.Admission == WorkAdmission.Startup)))
        {
            throw new InvalidOperationException("Work is outside the active lifecycle authority.");
        }
    }

    internal void Close()
    {
        lock (_module.Runtime.Sync)
        {
            _active = false;
        }
    }
}
