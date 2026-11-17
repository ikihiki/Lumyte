using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Channels;
using Lumyte.UI.WebView;

namespace Lumyte.UI.Application.WebView.Cef;

// All native code lives in the explicitly selected executable. Owner never waits for IPC.
internal sealed class CefHelperSession : IDisposable
{
    private readonly object _gate = new();
    private readonly CefWebViewOptions _options;
    private readonly WebViewPixelBudget _budget;
    private readonly Action _wake;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<CefHelperPacket> _commands = Channel.CreateBounded<CefHelperPacket>(new BoundedChannelOptions(16) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _lifetime;
    private readonly CefExecutionScope _execution;
    private readonly bool _ownsExecution;
    private CefSharedFrames? _sharedFrames;
    private bool _runtimeRetired;
    private ExceptionDispatchInfo? _cleanupFailure;
    private ExceptionDispatchInfo? _retirementFailure;
    private Process? _cleanupOwner;
    private string? _ownedDirectory;
    private bool _cleanupProven = true;
    private HelperSnapshot _snapshot;
    private bool _dirty = true;
    private bool _closing;
    private bool _ready;
    private CefHelperPacket? _pendingViewport;
    private long _generation;
    private long _eventSequence;
    private int _width = 800;
    private int _height = 600;
    private long _navigationStarted;
    private bool _loading = true;
    private int _disposed;

    internal CefHelperSession(Uri uri, WebViewPixelBudget budget, Action wake, CefWebViewOptions options, CefExecutionScope? execution = null, bool ownsExecution = false)
    {
        _options = options;
        _budget = budget;
        _wake = wake;
        _snapshot = new(UiWebViewState.Creating, uri);
        ViewPixels = new(budget);
        PopupPixels = new(budget);
        _execution = execution ?? new();
        _ownsExecution = execution is null || ownsExecution;
        _runtimeRetired = !_ownsExecution;
        _lifetime = RunLifetimeAsync(uri);
    }

    internal WebViewPixelMailbox ViewPixels
    {
        get;
    }

    internal WebViewPixelMailbox PopupPixels
    {
        get;
    }

    internal bool IsClosed => _lifetime.IsCompleted && Volatile.Read(ref _cleanupProven) && Volatile.Read(ref _runtimeRetired);

    internal Task Completion => _lifetime;

    internal Process? CleanupOwner => _cleanupOwner;

    internal string? OwnedDirectory => _ownedDirectory;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Close();
        ViewPixels.Dispose();
        PopupPixels.Dispose();

        // Lifetime owns cancellation until all readers/writers have unwound.
    }

    internal static string SelectOwnedDirectory(string temporaryBase)
    {
        // Linux sockaddr_un.sun_path has 108 bytes, including the terminating NUL.
        // Chromium appends /org.chromium.Chromium.XXXXXX/SingletonSocket (43 bytes);
        // reserve 48 so a bounded native suffix change does not consume the terminator.
        const int MaximumRootBytes = 108 - 1 - 48;
        string name = "lumyte-browser-" + Guid.NewGuid().ToString("N");
        string candidate = Path.Combine(Path.GetFullPath(temporaryBase), name);
        return Encoding.UTF8.GetByteCount(candidate) <= MaximumRootBytes ? candidate : Path.Combine("/tmp", name);
    }

    internal bool TryTakeSnapshot(out HelperSnapshot snapshot)
    {
        lock (_gate)
        {
            snapshot = _snapshot;
            bool dirty = _dirty;
            _dirty = false;
            return dirty;
        }
    }

    internal void Send(WebViewCommand command) => throw new NotSupportedException("cef-navigation-commands-unavailable");

    internal void SetViewport(int width, int height, float scale, bool visible, long generation)
    {
        lock (_gate)
        {
            if (_closing || _snapshot.State == UiWebViewState.Faulted)
            {
                return;
            }

            double scaledX = Math.Ceiling(width * (double)scale);
            double scaledY = Math.Ceiling(height * (double)scale);
            if (!double.IsFinite(scaledX) || !double.IsFinite(scaledY) || scaledX > 4096 || scaledY > 4096)
            {
                Fail("cef-viewport-budget");
                return;
            }

            int pixelsX = (int)scaledX;
            int pixelsY = (int)scaledY;
            if (pixelsX < 0 || pixelsY < 0 || (long)pixelsX * pixelsY * 4 > CefHelperProtocol.MaximumBody)
            {
                Fail("cef-viewport-budget");
                return;
            }

            _generation = generation;
            ViewPixels.Resize(generation);
            PopupPixels.Resize(generation);

            // Hidden/zero-sized views use a bounded 1x1 surface; no UI image is uploaded.
            _width = visible ? Math.Max(1, pixelsX) : 1;
            _height = visible ? Math.Max(1, pixelsY) : 1;
            if (_sharedFrames is { } shared && (long)_width * _height * 4 > shared.Capacity)
            {
                Fail("cef-shared-frame-capacity");
                return;
            }

            var viewport = new CefHelperPacket(3, generation, Width: _width, Height: _height);
            if (_ready)
            {
                Queue(viewport);
            }
            else
            {
                _pendingViewport = viewport;
            }
        }
    }

    internal void Close()
    {
        lock (_gate)
        {
            _closing = true;
        }

        _stop.Cancel();
    }

    internal void ThrowIfStopFailed()
    {
        Volatile.Read(ref _cleanupFailure)?.Throw();
        Volatile.Read(ref _retirementFailure)?.Throw();
    }

    internal async Task RetryStopAsync()
    {
        Close();
        Volatile.Read(ref _cleanupFailure)?.Throw();
        try
        {
            await _lifetime.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException) when (!_lifetime.IsCompleted)
        {
            ReportCleanupFailure();
            ThrowIfStopFailed();
        }
        catch (Exception) when (Volatile.Read(ref _retirementFailure) is not null)
        {
            // The workflow result was already observed; retry only its owned retirement.
        }

        if (_ownsExecution && !Volatile.Read(ref _runtimeRetired))
        {
            await RetireExecutionAsync();
        }

        ThrowIfStopFailed();
        _wake();
    }

    private static async Task DrainDiagnosticsAsync(StreamReader reader, CancellationToken token)
    {
        // Never retain unbounded native stderr or leak addresses/profile information to UI.
        char[] buffer = new char[1024];
        while (await reader.ReadAsync(buffer, token) != 0)
        {
        }
    }

    private void Queue(CefHelperPacket packet)
    {
        if (!_commands.Writer.TryWrite(packet))
        {
            Fail("cef-command-queue-full");
        }
    }

    private void Fail(string reason)
    {
        lock (_gate)
        {
            if (!_closing && _snapshot.State != UiWebViewState.Faulted)
            {
                _snapshot = _snapshot with { State = UiWebViewState.Faulted, FailureCode = reason, Loading = false };
                _dirty = true;
            }
        }

        _stop.Cancel();
        _wake();
    }

    private async Task RunLifetimeAsync(Uri uri)
    {
        try
        {
            await _execution.ExecuteAsync(() => RunAsync(uri), _stop.Token);
        }
        catch (Exception)
        {
            Fail("cef-execution-unavailable");
            if (!Volatile.Read(ref _cleanupProven))
            {
                ReportCleanupFailure();
            }
        }
        finally
        {
            try
            {
                if (_ownsExecution)
                {
                    await RetireExecutionAsync();
                }
            }
            finally
            {
                _wake();
            }
        }
    }

    private async Task RetireExecutionAsync()
    {
        try
        {
            await _execution.StopAsync();
            Volatile.Write(ref _runtimeRetired, true);
            Volatile.Write(ref _retirementFailure, null);
        }
        catch (Exception error)
        {
            Volatile.Write(ref _retirementFailure, ExceptionDispatchInfo.Capture(error));
            throw;
        }
    }

    private async Task RunAsync(Uri uri)
    {
        Process? process = null;
        Task? writer = null;
        Task? stderr = null;
        Task? navigation = null;
        string? ownedDirectory = null;
        bool cleanupComplete = true;
        try
        {
            if (_options.RequireIme)
            {
                Fail("cef-ime-unavailable");
                return;
            }

            if (_options.ProfileDirectory is not null)
            {
                Fail("cef-persistent-profile-unsupported");
                return;
            }

            string? path = _options.HelperExecutablePath;
            if (path is null || !Path.IsPathFullyQualified(path) || !File.Exists(path))
            {
                Fail("cef-helper-unavailable");
                return;
            }

            if (!OperatingSystem.IsLinux())
            {
                Fail("cef-helper-platform-unsupported");
                return;
            }

            string supervisor = _options.SupervisorExecutablePath ?? Path.Combine(Path.GetDirectoryName(path)!, "lumyte-cef-supervisor");
            if (!Path.IsPathFullyQualified(supervisor) || !File.Exists(supervisor))
            {
                Fail("cef-helper-supervisor-unavailable");
                return;
            }

            ownedDirectory = SelectOwnedDirectory(Path.GetTempPath());
            _ownedDirectory = ownedDirectory;

            // Set privacy atomically at creation, before launching any children.
            Directory.CreateDirectory(ownedDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            lock (_gate)
            {
                _sharedFrames = new CefSharedFrames(ownedDirectory, _budget);
                if (_pendingViewport is { } pending && (long)pending.Width * pending.Height * 4 > _sharedFrames.Capacity)
                {
                    Fail("cef-shared-frame-capacity");
                    return;
                }
            }

            // Native inherits these pipe endpoints directly into the helper and closes
            // its own fd0/fd1 references after spawn. No intermediary copies pixels.
            var start = new ProcessStartInfo(supervisor) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(path)! };
            start.ArgumentList.Add("--owner");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--root");
            start.ArgumentList.Add(ownedDirectory);
            start.ArgumentList.Add("--stop");
            start.ArgumentList.Add(Path.Combine(ownedDirectory, "stop"));
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(path);
            start.ArgumentList.Add("--lumyte-helper");
            if (_options.AllowUnsandboxed)
            {
                start.ArgumentList.Add("--lumyte-allow-unsandboxed");
            }

            start.Environment["TMPDIR"] = ownedDirectory;
            start.Environment["HOME"] = ownedDirectory;
            start.Environment["XDG_CACHE_HOME"] = ownedDirectory;
            start.Environment["XDG_CONFIG_HOME"] = ownedDirectory;
            start.Environment["XDG_DATA_HOME"] = ownedDirectory;
            _stop.Token.ThrowIfCancellationRequested();
            process = Process.Start(start) ?? throw new IOException("cef-helper-start-failed");
            _cleanupOwner = process;
            Volatile.Write(ref _cleanupProven, false);
            stderr = DrainDiagnosticsAsync(process.StandardError, _stop.Token);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            startup.CancelAfter(_options.StartupTimeout);
            CefHelperPacket startPacket;
            lock (_gate)
            {
                // OnPaint may precede Ready. Snapshot the latest requested geometry without
                // advancing the caller-owned generation (native Start is always generation 0).
                int initialWidth = _pendingViewport?.Width ?? 800;
                int initialHeight = _pendingViewport?.Height ?? 600;
                if ((long)initialWidth * initialHeight * 4 > _sharedFrames.Capacity)
                {
                    if (_pendingViewport is not null)
                    {
                        Fail("cef-shared-frame-capacity");
                        return;
                    }

                    // Layout has not arrived: do not fault on an unrequested default surface.
                    // Validate the bounded bootstrap normally, but suppress owned pixels until
                    // SetViewport installs the actual generation with Resize.
                    initialWidth = 1;
                    initialHeight = 1;
                    _width = 1;
                    _height = 1;
                    ViewPixels.Resize(-1);
                }

                startPacket = new(1, Width: initialWidth, Height: initialHeight, Body: Encoding.UTF8.GetBytes(uri.AbsoluteUri));
            }

            await CefHelperProtocol.WriteAsync(process.StandardInput.BaseStream, startPacket, startup.Token);

            CefHelperPacket first;
            try
            {
                first = await CefHelperProtocol.ReadAsync(process.StandardOutput.BaseStream, startup.Token);
            }
            catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
            {
                Fail("cef-startup-timeout");
                return;
            }

            if (first.Kind == 104)
            {
                Fail("cef-helper-bootstrap-failed");
                return;
            }

            if (first.Kind != 101 || first.Sequence < 0 || !_sharedFrames.MatchesReady(first.Body))
            {
                throw new InvalidDataException("cef-helper-not-ready");
            }

            lock (_gate)
            {
                _snapshot = _snapshot with { State = UiWebViewState.Ready, Loading = true };
                _dirty = true;
                _navigationStarted = Stopwatch.GetTimestamp();
                _eventSequence = first.Sequence;
                _ready = true;
                if (_pendingViewport is { } viewport)
                {
                    Queue(viewport);
                    _pendingViewport = null;
                }
            }

            lock (_gate)
            {
                _sharedFrames.Rescan(ViewPixels, _generation, _width, _height);
            }

            writer = WriteCommandsAsync(process.StandardInput.BaseStream, _stop.Token);
            _wake();
            navigation = MonitorNavigationAsync(_stop.Token);
            while (!_stop.IsCancellationRequested)
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                lock (_gate)
                {
                    if (_loading)
                    {
                        TimeSpan remaining = _options.NavigationTimeout - Stopwatch.GetElapsedTime(_navigationStarted);
                        if (remaining <= TimeSpan.Zero)
                        {
                            Fail("cef-navigation-timeout");
                            break;
                        }

                        deadline.CancelAfter(remaining);
                    }
                }

                CefHelperPacket packet;
                try
                {
                    packet = await CefHelperProtocol.ReadAsync(process.StandardOutput.BaseStream, deadline.Token);
                }
                catch (OperationCanceledException) when (!_stop.IsCancellationRequested)
                {
                    Fail("cef-navigation-timeout");
                    break;
                }

                lock (_gate)
                {
                    if (packet.Sequence < 0 || packet.Sequence < _eventSequence)
                    {
                        throw new InvalidDataException("cef-protocol-sequence");
                    }

                    _eventSequence = packet.Sequence;
                    switch (packet.Kind)
                    {
                        case 102:
                            string address = Encoding.UTF8.GetString(packet.Body!);
                            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? observed) || observed.Scheme is not ("http" or "https"))
                            {
                                throw new InvalidDataException("cef-protocol-uri");
                            }

                            _loading = false;
                            _snapshot = _snapshot with { Uri = observed, Loading = false, Sequence = packet.Sequence };
                            _dirty = true;
                            break;
                        case 103:
                            if (packet.Generation > _generation || (packet.Generation == _generation && (packet.Width != _width || packet.Height != _height)))
                            {
                                throw new InvalidDataException("cef-protocol-geometry");
                            }

                            // Notice only; authoritative pixels and frame sequence are in the ring.
                            break;
                        case 104: Fail("cef-helper-fault"); break;
                        default: throw new InvalidDataException("cef-protocol-kind");
                    }

                    _sharedFrames.Rescan(ViewPixels, _generation, _width, _height);
                }

                _wake();
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            Fail("cef-startup-timeout");
        }
        catch (InvalidDataException)
        {
            Fail("cef-helper-protocol");
        }
        catch (EndOfStreamException)
        {
            Fail("cef-helper-exited");
        }
        catch (IOException error) when (error.Message == "cef-shared-budget")
        {
            Fail("cef-shared-budget");
        }
        catch (Exception)
        {
            Fail("cef-helper-unavailable");
        }
        finally
        {
            _stop.Cancel();
            _commands.Writer.TryComplete();
            if (process is not null)
            {
                // Never kill the cleanup owner first. Private stop is independent of IPC backpressure.
                try
                {
                    if (!process.HasExited)
                    {
                        try
                        {
                            File.WriteAllText(Path.Combine(ownedDirectory!, "stop"), string.Empty);
                        }
                        catch (DirectoryNotFoundException)
                        {
                            // The native reaper may already have removed its root.
                        }
                        catch (IOException)
                        {
                            ReportCleanupFailure();
                        }
                        catch (UnauthorizedAccessException)
                        {
                            ReportCleanupFailure();
                        }

                        try
                        {
                            process.StandardInput.Close();
                        }
                        catch (IOException)
                        {
                            ReportCleanupFailure();
                        }
                    }

                    Task exited = process.WaitForExitAsync();
                    try
                    {
                        await exited.WaitAsync(TimeSpan.FromSeconds(5));
                    }
                    catch (TimeoutException)
                    {
                        // A deadline is not a cleanup receipt. Keep ownership until
                        // the reaper actually exits; the rest of the UI stays usable.
                        ReportCleanupFailure();
                        await exited;
                    }

                    // 125 denotes unproven cleanup; other nonzero exits propagate
                    // helper failure only after the native reaper completed cleanup.
                    cleanupComplete = process.ExitCode is not (125 or 2) && !Directory.Exists(ownedDirectory);
                    if (!cleanupComplete)
                    {
                        ReportCleanupFailure();
                    }
                }
                catch (Exception)
                {
                    cleanupComplete = false;
                    ReportCleanupFailure();
                }

                if (cleanupComplete)
                {
                    process.Dispose();
                    _cleanupOwner = null;
                }
            }

            if (writer is not null)
            {
                try
                {
                    await writer;
                }
                catch (Exception)
                {
                }
            }

            if (stderr is not null)
            {
                try
                {
                    await stderr;
                }
                catch (Exception)
                {
                }
            }

            if (navigation is not null)
            {
                await navigation;
            }

            Volatile.Write(ref _cleanupProven, cleanupComplete);
            if (cleanupComplete)
            {
                Volatile.Write(ref _cleanupFailure, null);
            }

            if (ownedDirectory is not null && cleanupComplete)
            {
                try
                {
                    Directory.Delete(ownedDirectory, recursive: true);
                }
                catch (IOException)
                {
                }
            }

            if (cleanupComplete)
            {
                _sharedFrames?.Dispose();
                _sharedFrames = null;
                _ownedDirectory = null;
            }

            _wake();
        }
    }

    private void ReportCleanupFailure()
    {
        lock (_gate)
        {
            _cleanupFailure ??= ExceptionDispatchInfo.Capture(new IOException("cef-helper-cleanup-failed"));
            _snapshot = _snapshot with { State = UiWebViewState.Faulted, FailureCode = "cef-helper-cleanup-failed", Loading = false };
            _dirty = true;
        }

        _wake();
    }

    private async Task MonitorNavigationAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(50, token);
                lock (_gate)
                {
                    if (_loading && Stopwatch.GetElapsedTime(_navigationStarted) >= _options.NavigationTimeout)
                    {
                        Fail("cef-navigation-timeout");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task WriteCommandsAsync(Stream stream, CancellationToken token)
    {
        try
        {
            await foreach (CefHelperPacket packet in _commands.Reader.ReadAllAsync(token))
            {
                await CefHelperProtocol.WriteAsync(stream, packet, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            Fail("cef-helper-write-failed");
        }
    }
}
