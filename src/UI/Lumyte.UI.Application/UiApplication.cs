using Lumyte.UI.TextEditing;
using Lumyte.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application;

/// <summary>Owns the UI loop and resources from startup through ordered shutdown.</summary>
public sealed partial class UiApplication : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly UiApplicationOptions _options;
    private readonly UiApplicationDispatcher _dispatcher;
    private readonly ServiceProvider _services;
    private readonly Func<UiApplicationDriver> _driverFactory;
    private readonly Func<UiApplicationContext, IReadOnlyList<UiWindowDeclaration>> _buildWindows;
    private readonly string _primaryKey;
    private readonly Func<UiWindowManagerContext, IUiWindowManager>? _managerFactory;
    private readonly Func<IServiceProvider, IUiTextLayoutProvider>? _textFactory;
    private readonly Func<IServiceProvider, IUiApplicationParticipant>[] _participantFactories;
    private readonly List<IUiApplicationParticipant> _participants = [];
    private readonly CancellationTokenSource _startupCancellation = new();
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<UiDriverWindow, UiSurface> _auxInput = [];
    private readonly HashSet<string> _failedKeys = new(StringComparer.Ordinal);
    private readonly List<TaskCompletionSource> _frameWaiters = [];
    private TaskCompletionSource _turn = NewCompletion();
    private TaskCompletionSource _attempt = NewCompletion();
    private Task? _startup;
    private Task? _stopping;
    private Task? _reconciling;
    private UiApplicationDriver? _driver;
    private IUiWindowManager? _windows;
    private IUiWindowManager? _defaultWindows;
    private UiPopupManager? _popups;
    private UiDriverWindow? _primary;
    private UiHostInputRouter? _router;
    private Exception? _failure;
    private Exception? _executionFailure;
    private int _state;
    private int _stopRequested;
    private int _rebuildRequested;
    private bool _started;
    private Task? _beforeStartStop;
    private volatile bool _retryRequested;
    private bool _providerReleased;
    private bool _driverReleased;
    private bool _managersStopped;
    private bool _participantsStopping;
    private bool _pumpFailed;

    internal UiApplication(string[] args, UiApplicationOptions options, UiApplicationDispatcher dispatcher, ServiceProvider services, Func<UiApplicationDriver> driver, Func<UiApplicationContext, IReadOnlyList<UiWindowDeclaration>> windows, string primaryKey, Func<UiWindowManagerContext, IUiWindowManager>? manager, Func<IServiceProvider, IUiTextLayoutProvider>? text, Func<IServiceProvider, IUiApplicationParticipant>[] participants)
    {
        Arguments = Array.AsReadOnly(args.ToArray());
        _options = options;
        _dispatcher = dispatcher;
        _services = services;
        _driverFactory = driver;
        _buildWindows = windows;
        _primaryKey = primaryKey;
        _managerFactory = manager;
        _textFactory = text;
        _participantFactories = participants;
    }

    /// <summary>Gets copied command-line arguments.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Gets the observable lifecycle state.</summary>
    public UiApplicationState State => (UiApplicationState)Volatile.Read(ref _state);

    /// <summary>Gets the original execution or cleanup failure.</summary>
    public Exception? Failure => Volatile.Read(ref _failure);

    /// <summary>Gets the last rejected window declaration, without replacing existing windows.</summary>
    public Exception? LastWindowUpdateFailure { get; private set; }

    /// <summary>Gets the shared owner dispatcher.</summary>
    public IUiDispatcher Dispatcher => _dispatcher;

    /// <summary>Gets completion of the terminal lifecycle, remaining pending on retryable cleanup failure.</summary>
    public Task Completion => _completion.Task;

    internal UiApplicationDispatcher Owner => _dispatcher;

    internal IServiceProvider Services => _services;

    internal UiApplicationDriver Driver => _driver ?? throw new InvalidOperationException("The driver is not initialized.");

    internal IUiWindowManager Windows => _windows ?? throw new InvalidOperationException("The window manager is not initialized.");

    internal IEnumerable<IUiApplicationViewParticipant> ViewParticipants => _participants.OfType<IUiApplicationViewParticipant>();

    internal ulong GraphicsGeneration { get; private set; }

    internal UiPopupManager Popups => _popups ?? throw new InvalidOperationException("The popup coordinator is not initialized.");

    internal IUiTextLayoutProvider? TextLayoutProvider { get; private set; }

    internal TimeProvider TimeProvider => _options.TimeProvider;

    internal UiApplicationOptions Options => _options;

    internal bool IsTurning { get; private set; }

    internal SemaphoreSlim InputDomain { get; } = new(1, 1);

    internal bool HasInputLease { get; set; }

    /// <summary>Creates a configuration builder without native side effects.</summary>
    /// <param name="args">Arguments copied by the builder.</param>
    /// <returns>The mutable builder.</returns>
    public static UiApplicationBuilder CreateBuilder(string[]? args = null) => new(args);

    /// <summary>Starts exactly once; cancellation requests orderly shutdown.</summary>
    /// <param name="cancellationToken">Requests stopping, never abandons cleanup.</param>
    /// <returns>The first terminal or retryable-stop attempt.</returns>
    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_started || State != UiApplicationState.Created)
            {
                throw new InvalidOperationException("The application has already been started.");
            }

            _started = true;
            SetState(UiApplicationState.Starting);
            _driver = _driverFactory();
            CancellationTokenRegistration registration = cancellationToken.Register(RequestStop);
            _ = _attempt.Task.ContinueWith(_ => registration.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (_driver.CurrentThread)
            {
                _ = RunBrowserLoopAsync();
            }
            else
            {
                var thread = new Thread(RunThreadLoop) { IsBackground = true, Name = "Lumyte UI" };
                if (OperatingSystem.IsWindows())
                {
                    thread.SetApartmentState(ApartmentState.STA);
                }

                thread.Start();
            }

            return _attempt.Task;
        }
    }

    /// <summary>Queues an idempotent stop request, including from callbacks.</summary>
    public void RequestStop() => Interlocked.Exchange(ref _stopRequested, 1);

    /// <summary>Waits for the shared stop attempt; caller cancellation affects only its wait.</summary>
    /// <param name="cancellationToken">Cancels this caller's wait.</param>
    /// <returns>The stop attempt.</returns>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        RequestStop();
        Task attempt;
        lock (_gate)
        {
            if (!_started)
            {
                _started = true;
                SetState(UiApplicationState.Stopping);
                _beforeStartStop = StopBeforeStartAsync(CancellationToken.None);
                return _beforeStartStop.WaitAsync(cancellationToken);
            }

            attempt = _attempt.Task;
        }

        return attempt.WaitAsync(cancellationToken);
    }

    /// <summary>Retries only the failed retryable stop stage.</summary>
    /// <param name="cancellationToken">Cancels this caller's wait.</param>
    /// <returns>The next stop attempt.</returns>
    public Task RetryStopAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (State != UiApplicationState.StopFailed)
            {
                throw new InvalidOperationException("Only failed cleanup can be retried.");
            }

            if (!_retryRequested)
            {
                _attempt = NewCompletion();
                _retryRequested = true;
            }

            return _attempt.Task.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Waits for shutdown; terminal DI release is never invoked twice.</summary>
    /// <returns>The final release completion.</returns>
    public async ValueTask DisposeAsync()
    {
        if (State == UiApplicationState.StopFailed)
        {
            await RetryStopAsync();
        }
        else
        {
            await StopAsync();
        }
    }

    internal void FailInput(Exception error)
    {
        RecordFailure(error);
        RequestStop();
    }

    internal void RequestRebuildWindows() => Interlocked.Exchange(ref _rebuildRequested, 1);

    internal ValueTask NextTurnAsync() => new(_turn.Task);

    internal async ValueTask NextFrameAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource waiter = NewCompletion();
        await _dispatcher.InvokeAsync(() => _frameWaiters.Add(waiter), cancellationToken);
        await waiter.Task.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SetState(UiApplicationState state) => Volatile.Write(ref _state, (int)state);

    private void RunThreadLoop()
    {
        InitializeOwner();
        while (!Completion.IsCompleted)
        {
            Turn();
            Thread.Sleep(_options.FrameInterval < TimeSpan.FromMilliseconds(10) ? _options.FrameInterval : TimeSpan.FromMilliseconds(10));
        }
    }

    private async Task RunBrowserLoopAsync()
    {
        InitializeOwner();
        while (!Completion.IsCompleted)
        {
            Turn();
            try
            {
                await Driver.NextTurnAsync(_options.FrameInterval);
            }
            catch (Exception error)
            {
                RecordFailure(new UiApplicationException("platform-unavailable", error));
                RequestStop();
                await Task.Delay(10);
            }
        }
    }

    private void InitializeOwner()
    {
        _dispatcher.BindOwner();
        _startup = _dispatcher.TrackAsync(StartAsync);
    }

    private void Turn()
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(_dispatcher);
        IsTurning = true;
        try
        {
            CompleteFrames();
            if (!_driverReleased && !_pumpFailed)
            {
                try
                {
                    Driver.Pump();
                }
                catch (Exception error)
                {
                    _pumpFailed = true;
                    RecordFailure(error);
                    RequestStop();
                }
            }

            _windows?.Advance();
            _popups?.Advance();
            DiscoverPopupText();
            Driver.DispatchPendingInput();
            PumpExecution();
            _dispatcher.Pump();
            foreach (Entry entry in _entries.Values.ToArray())
            {
                if (entry.Content is { IsStopped: false } content)
                {
                    content.Advance();
                }

                entry.Context.Chrome?.Advance();
                entry.Slot?.Advance();
            }

            AdvanceText();
            foreach (IUiApplicationParticipant participant in _participants)
            {
                participant.Advance();
            }

            ObserveStartup();
            if (Volatile.Read(ref _stopRequested) != 0)
            {
                if (_stopping is null || (State == UiApplicationState.StopFailed && _retryRequested))
                {
                    _retryRequested = false;
                    SetState(UiApplicationState.Stopping);
                    _stopping = _dispatcher.TrackAsync(StopCoreAsync);
                }
            }
            else if (State == UiApplicationState.Running && _reconciling is null && Interlocked.Exchange(ref _rebuildRequested, 0) != 0)
            {
                _reconciling = _dispatcher.TrackAsync(() => ReconcileAsync(false));
            }

            ObserveReconciliation();
            AdvanceRendering();
            if (_stopping?.IsCompleted == true && State == UiApplicationState.Stopping)
            {
                if (_stopping.Exception is { } stopFailure)
                {
                    _failure = stopFailure.InnerException ?? stopFailure;
                    SetState(UiApplicationState.StopFailed);
                    _attempt.TrySetException(_failure);
                }
                else
                {
                    Finish();
                }
            }
        }
        catch (Exception error)
        {
            RecordFailure(error);
            RequestStop();
            if (_stopping is null)
            {
                SetState(UiApplicationState.Stopping);
                _stopping = _dispatcher.TrackAsync(StopCoreAsync);
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            IsTurning = false;
            TaskCompletionSource turn = _turn;
            _turn = NewCompletion();
            turn.TrySetResult();
        }
    }

    private void ObserveStartup()
    {
        if (_startup?.IsCompleted != true || State != UiApplicationState.Starting)
        {
            return;
        }

        if (_startup.Exception is { } error)
        {
            RecordFailure(error.InnerException ?? error);
            RequestStop();
        }
        else if (_startup.IsCanceled)
        {
            RequestStop();
        }
        else
        {
            SetState(UiApplicationState.Running);
        }
    }

    private void ObserveReconciliation()
    {
        if (_reconciling?.IsCompleted != true)
        {
            return;
        }

        if (_reconciling.Exception is { } error)
        {
            LastWindowUpdateFailure = error.InnerException ?? error;
            if (_entries.Values.Any(entry => entry.Stopping))
            {
                RecordFailure(LastWindowUpdateFailure);
                RequestStop();
            }
        }

        _reconciling = null;
    }

    private void RecordFailure(Exception error)
    {
        _executionFailure ??= error;
        _failure = _executionFailure;
    }

    private void Finish()
    {
        _failure = _executionFailure;
        _dispatcher.Close();
        foreach (TaskCompletionSource waiter in _frameWaiters)
        {
            waiter.TrySetException(new ObjectDisposedException(nameof(UiApplication)));
        }

        _frameWaiters.Clear();
        _startupCancellation.Dispose();
        InputDomain.Dispose();
        SetState(Failure is null ? UiApplicationState.Stopped : UiApplicationState.Failed);
        if (Failure is { } error)
        {
            _completion.TrySetException(error);
            _attempt.TrySetException(error);
        }
        else
        {
            _completion.TrySetResult();
            _attempt.TrySetResult();
        }
    }

    private async Task StopBeforeStartAsync(CancellationToken cancellationToken)
    {
        if (!_providerReleased)
        {
            _providerReleased = true;
            try
            {
                await _services.DisposeAsync();
            }
            catch (Exception error)
            {
                RecordFailure(error);
            }
        }

        Finish();
        await Completion.WaitAsync(cancellationToken);
    }
}
