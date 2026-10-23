using System.Diagnostics;
using System.Threading.Tasks.Sources;

namespace Lumyte.Execution;

/// <summary>Collects module definitions and executes bounded typed work on explicit owners.</summary>
/// <summary>Implements bounded execution scheduling and admission.</summary>
public sealed partial class ExecutionRuntime
{
    [ThreadStatic]
    private static ExecutionRuntime? _executing;

    private readonly List<ModuleDefinition> _modules = [];
    private readonly Dictionary<Type, ModuleDefinition> _moduleTypes = [];
    private readonly Queue<QueuedWork> _cpu = new();
    private readonly List<GroupDefinition> _hostGroups = [];
    private readonly CancellationTokenSource _startupCancellation = new();
    private readonly TaskCompletionSource _startup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _shutdown;
    private TaskCompletionSource? _quiet;
    private ExecutionOptions _options = new();
    private volatile ExecutionStatus _status;
    private volatile Exception? _failure;
    private CpuExecutionMode _cpuMode;
    private bool _prepareAttempted;
    private bool _cancelPending;
    private bool _pumping;
    private int _hostThread;
    private int _hostCursor;
    private int _maxCpu;
    private int _workers;

    // Unfinished commands, active updates, and one entry per graph.
    private int _pending;
    private int _runs;
    private int _commands;
    private int _cleanupRuns;
    private int _cleanupCommands;
    private int _graphNodes;
    private long _bytes;
    private long _cleanupBytes;
    private long _sequence;

    /// <summary>Initializes a new instance of the <see cref="ExecutionRuntime"/> class, collecting each module once.</summary>
    /// <param name="modules">The explicit module instances, commonly injected by DI.</param>
    public ExecutionRuntime(IEnumerable<IExecutionModule> modules)
    {
        IExecutionModule[] snapshot = modules.ToArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (IExecutionModule module in snapshot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(module.Name);
            var definition = new ModuleDefinition(this, module);
            if (!names.Add(definition.Name) || !_moduleTypes.TryAdd(definition.Type, definition))
            {
                throw new ArgumentException("Duplicate module name or implementation type.", nameof(modules));
            }

            _modules.Add(definition);
        }

        foreach (ModuleDefinition module in _modules)
        {
            var builder = new ModuleBuilder(module);
            try
            {
                module.Module!.Declare(builder);
            }
            finally
            {
                builder.Close();
            }
        }

        ResolveDependencies();
    }

    /// <summary>Gets the runtime lifecycle state.</summary>
    public ExecutionStatus Status => _status;

    /// <summary>Gets the first startup, update, or scheduler failure.</summary>
    public Exception? Failure => _failure;

    internal static ExecutionRuntime? Executing => _executing;

    internal object Sync { get; } = new();

    internal List<OwnerDefinition> Owners { get; } = [];

    internal List<ThreadDefinition> Threads { get; } = [];

    internal List<PhaseDefinition> Phases { get; } = [];

    internal List<WorkDefinition> Works { get; } = [];

    internal List<UpdateDefinition> Updates { get; } = [];

    internal List<Action> RangeClearers { get; } = [];

    /// <summary>Collects placement, validates capacity, and connects all endpoints once on the fixed host.</summary>
    /// <param name="placement">The placement policy for fixed-thread owners.</param>
    /// <param name="cpuMode">The CPU backend.</param>
    /// <param name="maxCpuConcurrency">One for cooperative execution, positive for thread pool execution.</param>
    /// <param name="options">Retained capacity budgets.</param>
    public void PrepareOnCurrentHost(OwnerPlacement placement, CpuExecutionMode cpuMode, int maxCpuConcurrency, ExecutionOptions options)
    {
        VerifyOutsideHandler();
        lock (Sync)
        {
            if (_prepareAttempted)
            {
                throw new InvalidOperationException("Preparation can only be attempted once.");
            }

            _prepareAttempted = true;
        }

        options.Validate();
        if (!Enum.IsDefined(placement) || !Enum.IsDefined(cpuMode) || maxCpuConcurrency <= 0
            || (cpuMode == CpuExecutionMode.Cooperative && maxCpuConcurrency != 1))
        {
            throw new ArgumentException("Invalid placement or CPU concurrency.");
        }

        if (OperatingSystem.IsBrowser() && (placement == OwnerPlacement.Dedicated || cpuMode == CpuExecutionMode.ThreadPool))
        {
            throw new PlatformNotSupportedException("Use host owners and cooperative CPU execution in the browser.");
        }

        _options = options;
        _cpuMode = cpuMode;
        _maxCpu = maxCpuConcurrency;
        _hostThread = Environment.CurrentManagedThreadId;
        foreach (ModuleDefinition module in _modules)
        {
            var builder = new ExecutionPlanBuilder(module);
            try
            {
                module.Module!.Configure(builder, placement);
            }
            finally
            {
                builder.Close();
            }
        }

        ValidatePlacement(placement);
        lock (EndpointConnections.Sync)
        {
            var endpoints = new HashSet<ICommandEndpoint>();
            foreach (WorkDefinition work in Works)
            {
                if (work.Endpoint.HasConnected || !endpoints.Add(work.Endpoint))
                {
                    throw new InvalidOperationException("An endpoint can be connected only once, even after shutdown.");
                }

                if (work.PayloadBytes > options.MaxPayloadBytes
                    || (work.Admission == WorkAdmission.Cleanup && work.PayloadBytes > options.ReservedCleanupPayloadBytes))
                {
                    throw new ArgumentException("A command cannot fit the configured payload budget.", nameof(options));
                }
            }

            // Connect is a nonthrowing assignment. No endpoint can accept work until Created is published.
            foreach (WorkDefinition work in Works)
            {
                work.Endpoint.Connect(work);
            }

            _status = ExecutionStatus.Created;
        }
    }

    /// <summary>Returns the already connected endpoint for normal or cleanup work.</summary>
    /// <typeparam name="TCommand">The command payload type.</typeparam>
    /// <typeparam name="TResult">The handler return type.</typeparam>
    /// <param name="key">The local work key.</param>
    /// <returns>The fixed endpoint.</returns>
    public CommandPort<TCommand, TResult> Bind<TCommand, TResult>(WorkKey<TCommand, TResult> key)
        where TCommand : struct
    {
        WorkDefinition<TCommand, TResult>? work = key.Definition;
        if (work?.Module.Runtime != this || work.Admission == WorkAdmission.Startup || Status is ExecutionStatus.Unprepared or ExecutionStatus.Stopped)
        {
            throw new InvalidOperationException("Invalid or unavailable work binding.");
        }

        return (CommandPort<TCommand, TResult>)work.Endpoint;
    }

    /// <summary>Starts dedicated threads and initializes modules in dependency order.</summary>
    /// <param name="token">The startup cancellation token.</param>
    /// <returns>The shared startup completion; host pumping must continue.</returns>
    public ValueTask StartAsync(CancellationToken token = default)
    {
        VerifyOutsideHandler();
        VerifyHost();
        lock (Sync)
        {
            if (_status == ExecutionStatus.Unprepared)
            {
                throw new InvalidOperationException("Prepare the runtime before starting it.");
            }

            if (_status != ExecutionStatus.Created)
            {
                return new ValueTask(_startup.Task);
            }

            _status = ExecutionStatus.Starting;
        }

        _ = StartCoreAsync(token);
        return new ValueTask(_startup.Task);
    }

    /// <summary>Closes admission, waits for quiet work, and releases resources in reverse dependency order.</summary>
    /// <param name="mode">Drain or cancel pending normal work.</param>
    /// <returns>The shared shutdown completion; failed cleanup can be retried.</returns>
    public ValueTask StopAsync(ShutdownMode mode = ShutdownMode.CancelPending)
    {
        VerifyOutsideHandler();
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        TaskCompletionSource completion;
        lock (Sync)
        {
            if (_status == ExecutionStatus.Unprepared)
            {
                throw new InvalidOperationException("Runtime was not prepared.");
            }

            _cancelPending |= mode == ShutdownMode.CancelPending;
            if (_status == ExecutionStatus.Stopped)
            {
                return ValueTask.CompletedTask;
            }

            if (_shutdown is { Task.IsCompleted: false })
            {
                Notify();
                return new ValueTask(_shutdown.Task);
            }

            if (_status == ExecutionStatus.Created)
            {
                _startup.TrySetResult();
            }

            _status = ExecutionStatus.Quiescing;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _shutdown = completion;
            Notify();
        }

        try
        {
            _startupCancellation.Cancel();
        }
        catch (Exception error)
        {
            lock (Sync)
            {
                _failure ??= error;
            }
        }

        _ = StopCoreAsync(completion);
        return new ValueTask(completion.Task);
    }

    /// <summary>Pumps startup and cleanup work, and normal host draining during quiescence.</summary>
    /// <param name="maxWorkItems">The positive work budget.</param>
    /// <param name="maxElapsed">The positive elapsed budget.</param>
    /// <returns>The number of handlers or canceled commands processed.</returns>
    public int PumpHostLifecycle(int maxWorkItems, TimeSpan maxElapsed)
    {
        EnterPump(maxWorkItems, maxElapsed);
        try
        {
            return DrainGroups(_hostGroups, ref _hostCursor, maxWorkItems, maxElapsed, true, ReadSequence());
        }
        finally
        {
            _pumping = false;
        }
    }

    /// <summary>Pumps bounded cooperative CPU work and graph propagation.</summary>
    /// <param name="maxWorkItems">The positive work budget.</param>
    /// <param name="maxElapsed">The positive elapsed budget.</param>
    /// <returns>The number of work and propagation steps.</returns>
    public int PumpJobs(int maxWorkItems, TimeSpan maxElapsed)
    {
        EnterPump(maxWorkItems, maxElapsed);
        try
        {
            return DrainCpu(maxWorkItems, maxElapsed, _cpuMode == CpuExecutionMode.Cooperative);
        }
        finally
        {
            _pumping = false;
        }
    }

    /// <summary>Runs a bounded host phase, maintaining fairness across turns.</summary>
    /// <param name="phase">The local phase.</param>
    /// <param name="maxWorkItems">The positive budget.</param>
    /// <param name="maxElapsed">The positive elapsed budget.</param>
    /// <returns>The processed count.</returns>
    public int RunHostPhase(PhaseKey phase, int maxWorkItems, TimeSpan maxElapsed)
    {
        PhaseDefinition? definition = phase.Definition;
        if (definition?.Module.Runtime != this)
        {
            throw new ArgumentException("Invalid host phase.", nameof(phase));
        }

        EnterPump(maxWorkItems, maxElapsed);
        long started = Stopwatch.GetTimestamp();
        try
        {
            if (!definition.InPass)
            {
                definition.Limit = ReadSequence();
                definition.UpdateCursor = 0;
                definition.InPass = true;
            }

            int count = DrainGroups(definition.Groups, ref definition.Cursor, maxWorkItems, maxElapsed, false, definition.Limit);
            while (count < maxWorkItems && Stopwatch.GetElapsedTime(started) < maxElapsed && definition.UpdateCursor < definition.Updates.Count)
            {
                UpdateDefinition update = definition.Updates[definition.UpdateCursor++];
                if (RunUpdate(update, null))
                {
                    count++;
                }
            }

            lock (Sync)
            {
                bool hasCommands = false;
                foreach (GroupDefinition group in definition.Groups)
                {
                    if (group.Head is { } head && head.Sequence <= definition.Limit && CanClaim(head, out _))
                    {
                        hasCommands = true;
                        break;
                    }
                }

                if (!hasCommands && definition.UpdateCursor == definition.Updates.Count)
                {
                    definition.InPass = false;
                }
            }

            return count;
        }
        finally
        {
            _pumping = false;
        }
    }

    internal static void VerifyOutsideHandler()
    {
        if (_executing is not null)
        {
            throw new InvalidOperationException("Handlers cannot submit, wait, pump, or change runtime lifecycle.");
        }
    }

    internal CommandEnqueueStatus Enqueue<TCommand, TResult>(WorkDefinition<TCommand, TResult> work, in TCommand command, CancellationToken token, ModuleContext? context, out IValueTaskSource<TResult>? completion)
        where TCommand : struct
    {
        completion = null;
        lock (Sync)
        {
            if (work.Module.Runtime != this)
            {
                throw new ArgumentException("Work belongs to another runtime.", nameof(work));
            }

            context?.Verify(work);
            if (context is null && work.Admission == WorkAdmission.Startup)
            {
                throw new InvalidOperationException("Startup commands require lifecycle authority.");
            }

            if ((context is null && _status != ExecutionStatus.Running)
                || (context is not null && _status is not (ExecutionStatus.Starting or ExecutionStatus.Stopping or ExecutionStatus.Quiescing or ExecutionStatus.Running)))
            {
                return CommandEnqueueStatus.NotRunning;
            }

            if (token.IsCancellationRequested)
            {
                return CommandEnqueueStatus.Canceled;
            }

            bool cleanup = context?.IsCleanup == true || (context is not null && _status == ExecutionStatus.Quiescing && work.Admission == WorkAdmission.Cleanup);
            if (!Reserve(1, 1, work.PayloadBytes, cleanup, 0))
            {
                return CommandEnqueueStatus.QueueFull;
            }

            PendingCommand<TCommand, TResult> pending;
            try
            {
                pending = new PendingCommand<TCommand, TResult>(work, in command, token, cleanup) { Lifecycle = context is not null };
                EnqueueAccepted(pending);
            }
            catch
            {
                Release(1, 1, work.PayloadBytes, cleanup, 0);
                throw;
            }

            completion = pending;
            Notify(false);
            return CommandEnqueueStatus.Accepted;
        }
    }

    internal bool Reserve(int runs, int commands, long bytes, bool cleanup, int graphNodes)
    {
        if (cleanup)
        {
            if (runs > _options.ReservedCleanupRuns - _cleanupRuns || commands > _options.ReservedCleanupCommands - _cleanupCommands || bytes > _options.ReservedCleanupPayloadBytes - _cleanupBytes)
            {
                return false;
            }

            _cleanupRuns += runs;
            _cleanupCommands += commands;
            _cleanupBytes += bytes;
        }
        else
        {
            if (runs > _options.MaxInFlightRuns - _runs || commands > _options.MaxCommands - _commands
                || bytes > _options.MaxPayloadBytes - _bytes || graphNodes > _options.MaxGraphNodes - _graphNodes)
            {
                return false;
            }

            _runs += runs;
            _commands += commands;
            _bytes += bytes;
            _graphNodes += graphNodes;
        }

        return true;
    }

    internal void Release(int runs, int commands, long bytes, bool cleanup, int graphNodes)
    {
        lock (Sync)
        {
            if (cleanup)
            {
                _cleanupRuns -= runs;
                _cleanupCommands -= commands;
                _cleanupBytes -= bytes;
            }
            else
            {
                _runs -= runs;
                _commands -= commands;
                _bytes -= bytes;
                _graphNodes -= graphNodes;
            }
        }
    }

    internal void EnqueueAccepted(QueuedWork work)
    {
        if (_pending++ == 0)
        {
            _quiet = null;
        }

        QueueReady(work);
    }

    internal void QueueReady(QueuedWork work)
    {
        work.Sequence = ++_sequence;
        if (work.Definition?.Group is { } group)
        {
            if (group.Tail is { } tail)
            {
                tail.Next = work;
            }
            else
            {
                group.Head = work;
            }

            group.Tail = work;
            group.Owner.Thread?.Wake?.Set();
        }
        else
        {
            _cpu.Enqueue(work);
        }
    }

    internal void Notify(bool wakeOwners = true)
    {
        if (wakeOwners)
        {
            foreach (ThreadDefinition thread in Threads)
            {
                thread.Wake?.Set();
            }
        }

        ScheduleWorkers();
    }

    internal void NotifyCancellation()
    {
        lock (Sync)
        {
            if (_status != ExecutionStatus.Stopped)
            {
                Notify();
            }
        }
    }

    internal bool IsDependency(ModuleDefinition module, ModuleDefinition target)
    {
        var visited = new HashSet<ModuleDefinition>();
        var remaining = new Stack<ModuleDefinition>(module.Dependencies);
        while (remaining.TryPop(out ModuleDefinition? next))
        {
            if (next == target)
            {
                return true;
            }

            if (visited.Add(next))
            {
                foreach (ModuleDefinition dependency in next.Dependencies)
                {
                    remaining.Push(dependency);
                }
            }
        }

        return false;
    }

    private void ResolveDependencies()
    {
        foreach (ModuleDefinition module in _modules)
        {
            foreach (Type type in module.DependencyTypes.Distinct())
            {
                if (!_moduleTypes.TryGetValue(type, out ModuleDefinition? dependency) || dependency == module)
                {
                    throw new ArgumentException("Missing or self-referential module dependency.");
                }

                module.Dependencies.Add(dependency);
            }
        }

        var ordered = new List<ModuleDefinition>();
        var added = new HashSet<ModuleDefinition>();
        while (ordered.Count < _modules.Count)
        {
            int before = ordered.Count;
            foreach (ModuleDefinition module in _modules.OrderBy(module => module.Name, StringComparer.Ordinal))
            {
                if (!added.Contains(module) && module.Dependencies.All(added.Contains))
                {
                    ordered.Add(module);
                    added.Add(module);
                }
            }

            if (before == ordered.Count)
            {
                throw new ArgumentException("Cyclic module dependency.");
            }
        }

        _modules.Clear();
        _modules.AddRange(ordered);
    }

    private void ValidatePlacement(OwnerPlacement placement)
    {
        foreach (OwnerDefinition owner in Owners)
        {
            bool needsHost = owner.Requirement == OwnerRequirement.HostOnly || placement == OwnerPlacement.Host;
            if ((needsHost && owner.Phase is null) || (!needsHost && owner.Thread is null))
            {
                throw new InvalidOperationException("Every owner must match the selected placement policy.");
            }

            if (needsHost)
            {
                owner.ThreadId = _hostThread;
                _hostGroups.AddRange(owner.Groups);
            }
        }

        foreach (UpdateDefinition update in Updates)
        {
            if (update.Owner.Phase is { } phase)
            {
                if (update.Phase != phase || update.Options is not null)
                {
                    throw new InvalidOperationException("Host update must use its owner's phase.");
                }
            }
            else
            {
                ThreadDefinition thread = update.Owner.Thread!;
                ThreadLoopOptions options = update.Options ?? throw new InvalidOperationException("Dedicated update needs periodic options.");
                if (thread.Updates.Count > 0 && (thread.Options.Period != options.Period || thread.Options.MaxControlWorkItems != options.MaxControlWorkItems || thread.Options.MaxControlTime != options.MaxControlTime))
                {
                    throw new InvalidOperationException("Updates sharing a thread must share period and budgets.");
                }

                thread.Options = options;
                thread.Updates.Add(update);
            }
        }
    }

    private async Task StartCoreAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _startupCancellation.Token);
        try
        {
            lock (Sync)
            {
                foreach (ThreadDefinition definition in Threads)
                {
                    definition.Wake = new AutoResetEvent(false);
                    var thread = new Thread(() => ThreadLoop(definition)) { IsBackground = true, Name = definition.Module.Name + "." + definition.Name };
                    thread.Start();
                    definition.PhysicalThread = thread;
                }
            }

            foreach (ModuleDefinition module in _modules)
            {
                linked.Token.ThrowIfCancellationRequested();
                var context = new ModuleContext(module, false);
                module.StartAttempted = true;
                try
                {
                    await module.Module!.StartAsync(context, linked.Token).ConfigureAwait(false);
                    lock (Sync)
                    {
                        module.Initialized = true;
                    }
                }
                finally
                {
                    context.Close();
                }
            }

            lock (Sync)
            {
                linked.Token.ThrowIfCancellationRequested();
                if (_status == ExecutionStatus.Starting)
                {
                    long now = Stopwatch.GetTimestamp();
                    foreach (UpdateDefinition update in Updates)
                    {
                        update.Next = now;
                        update.Previous = now;
                    }

                    _status = ExecutionStatus.Running;
                    Notify();
                }
            }

            _startup.TrySetResult();
        }
        catch (Exception error)
        {
            lock (Sync)
            {
                _failure ??= error;
                if (_status == ExecutionStatus.Starting)
                {
                    _status = ExecutionStatus.Quiescing;
                    _cancelPending = true;
                    Notify();
                }
            }

            _startup.TrySetException(error);
        }
    }

    private async Task StopCoreAsync(TaskCompletionSource completion)
    {
        try
        {
            try
            {
                await _startup.Task.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Partial initialization is cleaned below on its original owner.
            }

            Task quiet;
            lock (Sync)
            {
                quiet = _pending == 0 ? Task.CompletedTask : (_quiet ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            await quiet.ConfigureAwait(false);
            lock (Sync)
            {
                _status = ExecutionStatus.Stopping;
            }

            for (int i = _modules.Count - 1; i >= 0; i--)
            {
                ModuleDefinition module = _modules[i];
                if (!module.StartAttempted || module.Stopped)
                {
                    continue;
                }

                var context = new ModuleContext(module, true);
                try
                {
                    await module.Module!.StopAsync(context).ConfigureAwait(false);
                    lock (Sync)
                    {
                        module.Stopped = true;
                    }
                }
                finally
                {
                    context.Close();
                }
            }

            lock (Sync)
            {
                quiet = _pending == 0 ? Task.CompletedTask : (_quiet ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            await quiet.ConfigureAwait(false);
            foreach (ThreadDefinition thread in Threads)
            {
                lock (Sync)
                {
                    thread.Exit = true;
                    thread.Wake?.Set();
                }

                thread.PhysicalThread?.Join();
                lock (Sync)
                {
                    // Notifications use the same lock until shutdown is published.
                    thread.PhysicalThread = null;
                    thread.Wake?.Dispose();
                    thread.Wake = null;
                }
            }

            lock (Sync)
            {
                foreach (WorkDefinition work in Works)
                {
                    work.Endpoint.Close();
                    work.Clear();
                }

                foreach (UpdateDefinition update in Updates)
                {
                    update.Clear();
                }

                foreach (Action clear in RangeClearers)
                {
                    clear();
                }

                RangeClearers.Clear();
                foreach (ModuleDefinition module in _modules)
                {
                    module.Module = null;
                }

                _startupCancellation.Dispose();
                _status = ExecutionStatus.Stopped;
            }

            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private void EnterPump(int maxWorkItems, TimeSpan maxElapsed)
    {
        VerifyOutsideHandler();
        VerifyHost();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWorkItems);
        if (maxElapsed <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxElapsed));
        }

        if (_pumping || _status == ExecutionStatus.Unprepared)
        {
            throw new InvalidOperationException("Pump requires preparation and cannot be reentered.");
        }

        _pumping = true;
    }

    private void VerifyHost()
    {
        if (_hostThread != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("Operation requires the fixed host thread.");
        }
    }

    private long ReadSequence()
    {
        lock (Sync)
        {
            return _sequence;
        }
    }
}
