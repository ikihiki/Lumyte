using System.Reflection;
using System.Runtime.InteropServices;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Application.WebView.Cef;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Retryable stop failures retain native ownership without blocking the UI owner.</summary>
public sealed class CefStopRecoveryTests
{
    /// <summary>A stopped reaper fails the stop attempt finitely, then the same reaper can resume and retire.</summary>
    /// <returns>The verification.</returns>
    [CefHelperFact]
    public async Task PausedSupervisorFailsStopAttemptAndCanRecoverAsync()
    {
        string directory = CopyHelper();
        string marker = Path.Combine(directory, "supervisor.txt");
        string wrapper = Path.Combine(directory, "supervisor-wrapper");
        string supervisor = Path.Combine(directory, "lumyte-cef-supervisor");
        string script = $"""
            #!/bin/sh
            printf '%s\n%s\n' "$$" "$4" > '{marker}'
            exec '{supervisor}' "$@"

            """;
        File.WriteAllText(wrapper, script);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        UiWebViewSession? browser = null;
        CefWebViewParticipant? participant = null;
        var options = new CefWebViewOptions { HelperExecutablePath = Path.Combine(directory, "fake-backpressure"), SupervisorExecutablePath = wrapper };
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) }).UseHeadless();

        // Exercise the real participant with an engine-free headless owner; do not relax product admission.
        typeof(UiApplicationBuilder).GetProperty("IntegrationDriverKind", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(builder, "desktop");
        builder.UseCefWebViews(options).UseRoot(window =>
        {
            participant = window.Services.GetRequiredService<CefWebViewParticipant>();
            browser = window.GetWebViewSession("browser", new("http://127.0.0.1/"));
            return Lumyte.UI.Compose.Panel();
        });
        UiApplication app = builder.Build();
        Task running = app.RunAsync();
        SafeFileHandle? pidfd = null;
        Task? stopping = null;
        try
        {
            await UntilAsync(() => browser?.State == UiWebViewState.Ready && File.Exists(marker));
            string[] receipt = File.ReadAllLines(marker);
            int pid = int.Parse(receipt[0], System.Globalization.CultureInfo.InvariantCulture);
            pidfd = NativeSignals.Open(pid);
            NativeSignals.Send(pidfd, 19);
            stopping = app.StopAsync();
            IOException failure = await Assert.ThrowsAsync<IOException>(() => stopping.WaitAsync(TimeSpan.FromSeconds(8)));
            Assert.Equal("cef-helper-cleanup-failed", failure.Message);
            Assert.Equal(UiApplicationState.StopFailed, app.State);
            Assert.False(app.Completion.IsCompleted);
            Assert.True(Directory.Exists(receipt[1]));
            var advanced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.IsAssignableFrom<SynchronizationContext>(app.Dispatcher).Post(_ => advanced.SetResult(), null);
            await advanced.Task.WaitAsync(TimeSpan.FromSeconds(2));
            NativeSignals.Send(pidfd, 18);
            await UntilAsync(() => !Directory.Exists(receipt[1]));

            // Unlink is not the managed cleanup receipt: join the retained session before retry.
            FieldInfo windowsField = typeof(CefWebViewParticipant).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var windows = (System.Collections.IDictionary)windowsField.GetValue(participant)!;
            object window = windows.Values.Cast<object>().Single();
            var entries = (System.Collections.IDictionary)window.GetType().GetProperty("Entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            object entry = entries.Values.Cast<object>().Single();
            var engine = (CefHelperSession)entry.GetType().GetProperty("Engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry)!;
            await engine.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            await app.RetryStopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(UiApplicationState.Stopped, app.State);
        }
        finally
        {
            if (pidfd is not null)
            {
                NativeSignals.TryResume(pidfd);
                pidfd.Dispose();
            }

            if (app.State == UiApplicationState.StopFailed)
            {
                try
                {
                    await app.RetryStopAsync().WaitAsync(TimeSpan.FromSeconds(8));
                }
                catch (Exception)
                {
                }
            }
            else
            {
                try
                {
                    await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(8));
                }
                catch (Exception)
                {
                }
            }

            try
            {
                await running.WaitAsync(TimeSpan.FromSeconds(8));
            }
            catch (Exception)
            {
            }

            if (app.Completion.IsCompleted)
            {
                await app.DisposeAsync();
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>Completed stop failures are observed on the owner and can retry the retained runtime.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task ParticipantObservesFailedStopTaskAndRetriesAsync()
    {
        var participant = new CefWebViewParticipant(new(), new TestDispatcher());
        participant.BeginStop();
        FieldInfo stopping = typeof(CefWebViewParticipant).GetField("_executionStop", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await ((Task)stopping.GetValue(participant)!).WaitAsync(TimeSpan.FromSeconds(5));
        var original = new IOException("fixture-runtime-stop");
        var failed = Task.FromException(original);
        stopping.SetValue(participant, failed);
        try
        {
            IOException actual = Assert.Throws<IOException>(() => participant.IsStopped);
            Assert.Same(original, actual);
            await UntilAsync(() => participant.IsStopped);
            await participant.DisposeAsync();
        }
        finally
        {
            _ = failed.Exception;
        }
    }

    /// <summary>An actual module cleanup failure reaches StopFailed, preserving the original exception and allowing retry.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task RuntimeStopFailureReachesApplicationAndRetriesAsync()
    {
        var original = new IOException("fixture-runtime-stop");
        int attempts = 0;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) }).UseHeadless();
        typeof(UiApplicationBuilder).GetProperty("IntegrationDriverKind", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(builder, "desktop");
        builder.UseCefWebViews(new()).UseRoot(_ => Lumyte.UI.Compose.Panel());
        builder.Services.Replace(ServiceDescriptor.Singleton(provider => new CefWebViewParticipant(new(), provider.GetRequiredService<IUiDispatcher>(), new(() => ++attempts == 1 ? ValueTask.FromException(original) : ValueTask.CompletedTask))));
        UiApplication app = builder.Build();
        Task running = app.RunAsync();
        try
        {
            await UntilAsync(() => app.State == UiApplicationState.Running);
            IOException actual = await Assert.ThrowsAsync<IOException>(() => app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(original, actual);
            Assert.Equal(UiApplicationState.StopFailed, app.State);
            Assert.False(app.Completion.IsCompleted);
            var advanced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.IsAssignableFrom<SynchronizationContext>(app.Dispatcher).Post(_ => advanced.SetResult(), null);
            await advanced.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await app.RetryStopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(UiApplicationState.Stopped, app.State);
            Assert.Equal(2, attempts);
        }
        finally
        {
            if (app.State == UiApplicationState.StopFailed)
            {
                await app.RetryStopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }

            try
            {
                await running.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (IOException error) when (ReferenceEquals(error, original))
            {
            }

            await app.DisposeAsync();
        }
    }

    /// <summary>Direct-session closure requires owned runtime retirement, not merely a completed faulted lifetime task.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task OwnedRuntimeRetirementFailureNeverClaimsClosedAsync()
    {
        var original = new IOException("fixture-owned-runtime-stop");
        int attempts = 0;
        var scope = new CefExecutionScope(() => ++attempts == 1 ? ValueTask.FromException(original) : ValueTask.CompletedTask);
        using var session = new CefHelperSession(new("http://127.0.0.1/"), new(1024), () => { }, new(), scope, ownsExecution: true);
        IOException actual = await Assert.ThrowsAsync<IOException>(() => session.Completion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(original, actual);
        Assert.False(session.IsClosed);
        Assert.Same(original, Assert.Throws<IOException>(session.ThrowIfStopFailed));
        await session.RetryStopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(session.IsClosed);
        Assert.Equal(2, attempts);
    }

    /// <summary>A terminal cleanup failure retains the native receipt and root, and never restarts the helper on retry.</summary>
    /// <returns>The verification.</returns>
    [CefHelperFact]
    public async Task FailedCleanupReceiptRetainsRootAndOwnerAsync()
    {
        string directory = CopyHelper();
        string supervisor = Path.Combine(directory, "failed-supervisor");
        File.WriteAllText(supervisor, "#!/bin/sh\nexit 125\n");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(supervisor, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var budget = new WebViewPixelBudget(1048576);
        using var session = new CefHelperSession(new("http://127.0.0.1/"), budget, () => { }, new()
        {
            HelperExecutablePath = Path.Combine(directory, "fake-backpressure"),
            SupervisorExecutablePath = supervisor,
        });
        try
        {
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(session.IsClosed);
            Assert.NotNull(session.CleanupOwner);
            Assert.True(session.CleanupOwner.HasExited);
            Assert.True(budget.Used > 0);
            Assert.NotNull(typeof(CefHelperSession).GetField("_sharedFrames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session));
            Assert.True(Directory.Exists(session.OwnedDirectory));
            IOException original = Assert.Throws<IOException>(session.ThrowIfStopFailed);
            Assert.Equal("cef-helper-cleanup-failed", original.Message);
            Assert.Same(original, await Assert.ThrowsAsync<IOException>(() => session.RetryStopAsync().WaitAsync(TimeSpan.FromSeconds(2))));
            Assert.False(session.IsClosed);
            Assert.True(Directory.Exists(session.OwnedDirectory));
        }
        finally
        {
            // This fixture exits without spawning anything. Production never substitutes
            // directory deletion for descendant-cleanup proof after a 125 receipt.
            session.CleanupOwner?.Dispose();
            (typeof(CefHelperSession).GetField("_sharedFrames", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session) as IDisposable)?.Dispose();
            if (session.OwnedDirectory is { } root)
            {
                Directory.Delete(root, true);
            }

            Directory.Delete(directory, true);
        }
    }

    private static string CopyHelper()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Lumyte.slnx")))
        {
            root = Directory.GetParent(root)!.FullName;
        }

        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string source = Path.Combine(root, "samples/Lumyte.UI.WebView.Helper.Sample/bin", configuration, "net10.0");
        string target = Path.Combine(Path.GetTempPath(), "lumyte-stop-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        File.Copy(Path.Combine(source, "Lumyte.UI.WebView.Helper.Sample"), Path.Combine(target, "fake-backpressure"));
        return target;
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition())
        {
            await Task.Delay(10, deadline.Token);
        }
    }

    private static class NativeSignals
    {
        internal static SafeFileHandle Open(int pid)
        {
            long fd = Syscall(434, pid, 0, 0, 0);
            Assert.True(fd >= 0, Marshal.GetLastPInvokeError().ToString(System.Globalization.CultureInfo.InvariantCulture));
            return new((nint)fd, ownsHandle: true);
        }

        internal static void Send(SafeFileHandle pidfd, int signal) => Assert.Equal(0, Syscall(424, (long)pidfd.DangerousGetHandle(), signal, 0, 0));

        internal static void TryResume(SafeFileHandle pidfd) => Syscall(424, (long)pidfd.DangerousGetHandle(), 18, 0, 0);

        // Variadic syscall has no LibraryImport source-generated signature; arguments here are scalar ABI words.
        [DllImport("libc", EntryPoint = "syscall", SetLastError = true)]
        private static extern long Syscall(long number, long first, long second, long third, long fourth);
    }

    private sealed class TestDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
