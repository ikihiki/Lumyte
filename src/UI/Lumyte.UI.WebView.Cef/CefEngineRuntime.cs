using Xilium.CefGlue;

namespace Lumyte.UI.WebView.Cef;

internal sealed class CefEngineRuntime : IDisposable
{
    private static int _everInitialized;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly Action _wake;
    private readonly EngineApp _app;
    private readonly HashSet<CefEngineSession> _sessions = [];
    private readonly FileStream? _profileLock;
    private long _deadline = long.MaxValue;
    private bool _disposed;

    internal CefEngineRuntime(string[] args, string? profileDirectory, Action wake)
    {
        if (OperatingSystem.IsWindows())
        {
            // This managed-only launch cannot prove the required pre-CLR native sandbox bootstrap.
            throw new PlatformNotSupportedException("cef-windows-native-sandbox-bootstrap-required");
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("cef-platform-unverified");
        }

        _wake = wake;
        _app = new EngineApp(this);
        if (Interlocked.Exchange(ref _everInitialized, 1) != 0)
        {
            throw new InvalidOperationException("cef-runtime-already-used");
        }

        try
        {
            if (profileDirectory is not null)
            {
                profileDirectory = Path.GetFullPath(profileDirectory);
                Directory.CreateDirectory(profileDirectory);
                _profileLock = new FileStream(Path.Combine(profileDirectory, ".lumyte-cef.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }

            CefRuntime.Load(); // Deliberately NOT CefGlue.Common's sandbox-disabling loader.
            var settings = new CefSettings
            {
                NoSandbox = false,
                WindowlessRenderingEnabled = true,
                MultiThreadedMessageLoop = false,
                ExternalMessagePump = true,
                CommandLineArgsDisabled = true,
                RemoteDebuggingPort = 0,
                LogSeverity = CefLogSeverity.Disable,
                BrowserSubprocessPath = Environment.ProcessPath,
                CachePath = profileDirectory ?? string.Empty,
                RootCachePath = profileDirectory ?? string.Empty,
                PersistSessionCookies = false,
                PersistUserPreferences = profileDirectory is not null,
            };
            CefRuntime.Initialize(MainArgs(args), settings, _app, IntPtr.Zero);
            Schedule(0);
        }
        catch
        {
            _profileLock?.Dispose();
            throw;
        }
    }

    internal TimeSpan? NextWorkDelay
    {
        get
        {
            long deadline = Interlocked.Read(ref _deadline);
            return deadline == long.MaxValue ? null : TimeSpan.FromMilliseconds(Math.Max(0, deadline - Environment.TickCount64));
        }
    }

    public void Dispose()
    {
        CheckOwner();
        if (_sessions.Any(s => !s.IsClosed))
        {
            throw new InvalidOperationException("cef-browsers-still-live");
        }

        if (!_disposed)
        {
            CefRuntime.Shutdown();
            _disposed = true;
            _profileLock?.Dispose();
        }
    }

    internal static bool TryRunSubprocess(string[] args, out int exitCode)
    {
        if (!args.Any(a => a.StartsWith("--type=", StringComparison.Ordinal)))
        {
            exitCode = 0;
            return false;
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("cef-native-sandbox-bootstrap-required");
        }

        if (args.Any(a => a.Equals("--no-sandbox", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--remote-debugging", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("cef-unsafe-subprocess-arguments");
        }

        CefRuntime.Load();
        exitCode = CefRuntime.ExecuteProcess(MainArgs(args), null, IntPtr.Zero);
        return exitCode >= 0;
    }

    internal CefEngineSession CreateSession(Uri uri, WebViewPixelBudget budget, Action wake)
    {
        CheckOwner();
        UiWebViewSession.ValidateUri(uri);
        var session = new CefEngineSession(this, uri, budget, wake);
        _sessions.Add(session);
        session.Create();
        return session;
    }

    internal void ReleaseSession(CefEngineSession session) => _sessions.Remove(session);

    internal void Pump()
    {
        CheckOwner();
        if (Interlocked.Read(ref _deadline) <= Environment.TickCount64)
        {
            Interlocked.Exchange(ref _deadline, long.MaxValue);
            CefRuntime.DoMessageLoopWork();
        }
    }

    internal void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _owner)
        {
            throw new InvalidOperationException("cef-owner-thread-required");
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static CefMainArgs MainArgs(string[] args) => new([Environment.ProcessPath!, .. args]);

    private void Schedule(long delay)
    {
        long due = Environment.TickCount64 + Math.Clamp(delay, 0, int.MaxValue);
        long old;
        do
        {
            old = Interlocked.Read(ref _deadline);
            if (old <= due)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _deadline, due, old) != old);
        _wake();
    }

    private sealed class EngineApp(CefEngineRuntime owner) : CefApp
    {
        private readonly ProcessHandler _handler = new(owner);

        protected override CefBrowserProcessHandler GetBrowserProcessHandler() => _handler;
    }

    private sealed class ProcessHandler(CefEngineRuntime owner) : CefBrowserProcessHandler
    {
        protected override void OnScheduleMessagePumpWork(long delay) => owner.Schedule(delay);
    }
}
