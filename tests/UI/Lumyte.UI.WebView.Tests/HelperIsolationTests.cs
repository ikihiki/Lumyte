using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Application.WebView.Cef;
using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Real IPC child failures must not escape into a UI owner.</summary>
public sealed class HelperIsolationTests
{
    /// <summary>Inherited temporary paths must leave room for Chromium singleton sockets, in bytes.</summary>
    /// <param name="temporaryBase">An inherited temporary base.</param>
    [CefHelperTheory]
    [InlineData("/tmp/ce-bwbv5o0s")]
    [InlineData("/tmp/very-long-private-launcher-runtime/another-nested-directory")]
    [InlineData("/tmp/日本語日本語日本語")]
    [InlineData("/tmp/éééé")]
    public void OwnedTemporaryRootFitsChromiumSocket(string temporaryBase)
    {
        string root = CefHelperSession.SelectOwnedDirectory(temporaryBase);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(root) + 48 < 108, root);
        Assert.Equal("/tmp", Path.GetDirectoryName(root));
        Assert.StartsWith("lumyte-browser-", Path.GetFileName(root), StringComparison.Ordinal);
    }

    /// <summary>A short caller base is honored and each browser has a distinct random root.</summary>
    [CefHelperFact]
    public void OwnedTemporaryRootsAreUniqueAndHonorShortBase()
    {
        string first = CefHelperSession.SelectOwnedDirectory("/tmp");
        string second = CefHelperSession.SelectOwnedDirectory("/tmp");
        Assert.Equal("/tmp", Path.GetDirectoryName(first));
        Assert.NotEqual(first, second);
        Assert.Equal(47, Path.GetFileName(first).Length);
        Assert.Equal("/tmp/a", Path.GetDirectoryName(CefHelperSession.SelectOwnedDirectory("/tmp/a")));
        Assert.Equal("/tmp/é", Path.GetDirectoryName(CefHelperSession.SelectOwnedDirectory("/tmp/é")));
    }

    /// <summary>Unsafe mode is never the default; explicit permission forwards exactly one dedicated flag without retry.</summary>
    /// <param name="allow">Explicit sandbox opt-out.</param>
    /// <returns>The verification.</returns>
    [CefHelperTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SandboxChoiceIsExplicitAndNeverRetriedAsync(bool allow)
    {
        Assert.False(new CefWebViewOptions().AllowUnsandboxed);
        string executable = CopyHelper("argumentproof");
        try
        {
            using var helper = new CefHelperSession(new("http://127.0.0.1/"), new(1048576), () => { }, new() { HelperExecutablePath = executable, AllowUnsandboxed = allow });
            await helper.Completion.WaitAsync(TimeSpan.FromSeconds(8));
            helper.TryTakeSnapshot(out HelperSnapshot snapshot);
            Assert.Equal(UiWebViewState.Faulted, snapshot.State);
            Assert.Equal("cef-helper-exited", snapshot.FailureCode);
            string[] launches = File.ReadAllLines(Path.Combine(Path.GetDirectoryName(executable)!, "launch-args.txt"));
            Assert.Single(launches);
            Assert.Equal(allow ? "--lumyte-helper --lumyte-allow-unsandboxed" : "--lumyte-helper", launches[0]);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(executable)!, true);
        }
    }

    /// <summary>Missing helpers fault locally and do not retry.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task MissingHelperDoesNotRetryAsync()
    {
        using CefHelperSession helper = Create(null);
        await helper.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(helper.TryTakeSnapshot(out HelperSnapshot snapshot));
        Assert.Equal(UiWebViewState.Faulted, snapshot.State);
        Assert.Equal("cef-helper-unavailable", snapshot.FailureCode);
        Assert.False(helper.TryTakeSnapshot(out _));
    }

    /// <summary>Crash, bad frames, startup and navigation hangs are bounded child faults.</summary>
    /// <param name="mode">Fake executable mode.</param>
    /// <param name="reason">Expected non-sensitive failure.</param>
    /// <returns>The verification.</returns>
    [CefHelperTheory]
    [InlineData("crash", "cef-helper-exited")]
    [InlineData("sigabrt", "cef-helper-exited")]
    [InlineData("malformed", "cef-helper-protocol")]
    [InlineData("sequence-rewind", "cef-helper-protocol")]
    [InlineData("hang", "cef-startup-timeout")]
    [InlineData("navigationhang", "cef-navigation-timeout")]
    public async Task ChildFailurePreservesOwnerAsync(string mode, string reason)
    {
        string executable = CopyHelper(mode);
        try
        {
            using CefHelperSession helper = Create(executable);
            int ownerProgress = 0;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (!helper.Completion.IsCompleted)
            {
                ownerProgress++;
                await Task.Delay(10, deadline.Token);
            }

            await helper.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            helper.TryTakeSnapshot(out HelperSnapshot snapshot);
            Assert.Equal(UiWebViewState.Faulted, snapshot.State);
            Assert.Equal(reason, snapshot.FailureCode);
            Assert.True(ownerProgress > 0);
            Assert.False(helper.TryTakeSnapshot(out _));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(executable)!, true);
        }
    }

    /// <summary>Explicit cancel releases child and latest frames without replay.</summary>
    /// <returns>The verification.</returns>
    [CefHelperFact]
    public async Task CancelAndRegenerationReleaseResourcesAsync()
    {
        string executable = CopyHelper("normal");
        var budget = new WebViewPixelBudget(1048576);
        try
        {
            using var helper = new CefHelperSession(new("https://example.test/"), budget, () => { }, new() { HelperExecutablePath = executable });
            await WaitAsync(() =>
            {
                helper.TryTakeSnapshot(out HelperSnapshot state);
                return state.State == UiWebViewState.Ready;
            });
            helper.SetViewport(4, 4, 1, true, 7);
            await WaitAsync(() => budget.Used > 0);
            using (WebViewPixelMailbox.PixelLease? frame = helper.ViewPixels.Take())
            {
                Assert.NotNull(frame);
                Assert.Equal(7, frame.Generation);
                Assert.Equal(64, frame.Bytes.Length);
            }

            helper.SetViewport(3, 3, 1, true, 8);
            await WaitAsync(() => budget.Used > 0);
            using (WebViewPixelMailbox.PixelLease? frame = helper.ViewPixels.Take())
            {
                Assert.Equal(8, frame!.Generation);
            }

            helper.Close();
            await helper.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            helper.Dispose();
            Assert.Equal(0, budget.Used);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(executable)!, true);
        }
    }

    /// <summary>Untrusted lengths are rejected before payload allocation.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task OversizePacketRejectedBeforeAllocationAsync()
    {
        byte[] header = new byte[44];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, int.MaxValue);
        await Assert.ThrowsAsync<InvalidDataException>(() => CefHelperProtocol.ReadAsync(new MemoryStream(header), CancellationToken.None));
    }

    /// <summary>A real native UI owner continues executing after an optional child failure.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task NativeUiOwnerContinuesAfterHelperFailureAsync()
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Lumyte.UI.Compose.Panel());
        await using UiApplication app = builder.Build();
        Task run = app.RunAsync();
        await WaitAsync(() => app.State == UiApplicationState.Running);
        using CefHelperSession helper = Create(null);
        await helper.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        int clicks = 0;
        await app.Dispatcher.InvokeAsync(() => clicks++);
        Assert.Equal(1, clicks);
        Assert.Equal(UiApplicationState.Running, app.State);
        Assert.Null(app.Failure);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run;
    }

    /// <summary>Unsupported input is observable; explicit browser cancellation preserves UI lifetime.</summary>
    [Fact]
    public void CapabilitiesAndCancellationAreExplicit()
    {
        bool cancelled = false;
        var session = new UiWebViewSession(new("https://example.test/"), () => { }, _ => { });
        session.CancelHost = () => cancelled = true;
        session.NavigationCommandsAvailable = false;
        Assert.Throws<NotSupportedException>(() => session.NavigateAsync(new("https://example.test/next")));
        Assert.False(session.InputAvailable);
        Assert.False(session.PopupAvailable);
        Assert.False(session.ImeAvailable);
        session.ReportUnsupportedInput();
        Assert.Equal("cef-input-unavailable", session.UnsupportedCapability);
        session.Cancel();
        Assert.True(cancelled);
        Assert.Equal(UiWebViewState.Faulted, session.State);
        Assert.Equal("browser-cancelled", session.FailureCode);
    }

    /// <summary>Explicit local qualification: sandbox failures remain local to the optional browser.</summary>
    /// <returns>The verification.</returns>
    [CefHelperFact(true)]
    public async Task ExplicitSandboxHelperCannotAbortUiOwnerAsync()
    {
        string? executable = Environment.GetEnvironmentVariable("LUMYTE_TEST_CEF_HELPER");
        if (executable is null)
        {
            return;
        }

        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().UseRoot(_ => Lumyte.UI.Compose.Panel());
        await using UiApplication app = builder.Build();
        Task run = app.RunAsync();
        await WaitAsync(() => app.State == UiApplicationState.Running);
        using CefHelperSession helper = Create(executable);
        await helper.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        helper.TryTakeSnapshot(out HelperSnapshot state);
        Assert.Equal(UiWebViewState.Faulted, state.State);
        await app.Dispatcher.InvokeAsync(() => Assert.Equal(UiApplicationState.Running, app.State));
        Assert.Null(app.Failure);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await run;
    }

    /// <summary>Cancel during initialization does not wait for the startup timeout.</summary>
    /// <returns>The verification.</returns>
    [CefHelperFact]
    public async Task StartupCancelIsBoundedAsync()
    {
        string executable = CopyHelper("hang");
        try
        {
            using CefHelperSession helper = Create(executable);
            helper.Close();
            await helper.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(helper.IsClosed);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(executable)!, true);
        }
    }

    /// <summary>Pre-ready layout is coalesced to its latest generation.</summary>
    /// <returns>The verification.</returns>
    [CefHelperFact]
    public async Task ViewportQueueIsBoundedAsync()
    {
        string executable = CopyHelper("slowready");
        try
        {
            var budget = new WebViewPixelBudget(1048576);
            using var helper = new CefHelperSession(new("https://example.test/"), budget, () => { }, new() { HelperExecutablePath = executable });
            for (int generation = 1; generation <= 100; generation++)
            {
                helper.SetViewport(2, 2, 1, true, generation);
            }

            await WaitAsync(() => budget.Used > 0);
            using WebViewPixelMailbox.PixelLease? frame = helper.ViewPixels.Take();
            Assert.NotNull(frame);
            Assert.Equal(100, frame.Generation);
            helper.TryTakeSnapshot(out HelperSnapshot snapshot);
            Assert.Equal(UiWebViewState.Ready, snapshot.State);
            helper.Close();
            await helper.Completion.WaitAsync(TimeSpan.FromSeconds(6));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(executable)!, true);
        }
    }

    /// <summary>A ready non-reading helper cannot grow the queue.</summary>
    /// <returns>The verification.</returns>
    [CefHelperFact]
    public async Task PostReadyBackpressureIsBoundedAsync()
    {
        string executable = CopyHelper("backpressure");
        try
        {
            using CefHelperSession helper = Create(executable);
            await WaitAsync(() =>
            {
                helper.TryTakeSnapshot(out HelperSnapshot s);
                return s.State == UiWebViewState.Ready;
            });
            for (int i = 1; i <= 100000; i++)
            {
                helper.SetViewport(1, 1, 1, true, i);
            }

            await helper.Completion.WaitAsync(TimeSpan.FromSeconds(6));
            helper.TryTakeSnapshot(out HelperSnapshot snapshot);
            Assert.Equal("cef-command-queue-full", snapshot.FailureCode);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(executable)!, true);
        }
    }

    /// <summary>Root exit must reap detached stdout-inheriting descendants.</summary>
    /// <param name="mode">The early-exit fixture mode.</param>
    /// <returns>The verification.</returns>
    [CefHelperTheory]
    [InlineData("orphan-0")]
    [InlineData("orphan-134")]
    public async Task EarlyExitReapsDetachedDescendantsAsync(string mode)
    {
        string executable = CopyHelper(mode);
        int child = 0;
        try
        {
            using CefHelperSession helper = Create(executable);
            string marker = Path.Combine(Path.GetDirectoryName(executable)!, "child.txt");
            await WaitAsync(() => File.Exists(marker));
            string[] lines = File.ReadAllLines(marker);
            child = int.Parse(lines[0], System.Globalization.CultureInfo.InvariantCulture);
            await helper.Completion.WaitAsync(TimeSpan.FromSeconds(6));
            Assert.False(Directory.Exists("/proc/" + child));
            Assert.False(Directory.Exists(lines[1]));
            Assert.Equal("700", lines[2]);
            Assert.True(System.Text.Encoding.UTF8.GetByteCount(lines[1]) + 48 < 108);
            helper.TryTakeSnapshot(out HelperSnapshot snapshot);
            Assert.Equal(UiWebViewState.Faulted, snapshot.State);
        }
        finally
        {
            if (child != 0 && Directory.Exists("/proc/" + child))
            {
                System.Diagnostics.Process.GetProcessById(child).Kill();
            }

            Directory.Delete(Path.GetDirectoryName(executable)!, true);
        }
    }

    private static CefHelperSession Create(string? executable) => new(new("https://example.test/"), new(1048576), () => { }, new() { HelperExecutablePath = executable, StartupTimeout = TimeSpan.FromSeconds(2), NavigationTimeout = TimeSpan.FromMilliseconds(400) });

    private static string CopyHelper(string mode)
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Lumyte.slnx")))
        {
            root = Directory.GetParent(root)!.FullName;
        }

        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        string source = Path.Combine(root, "samples/Lumyte.UI.WebView.Helper.Sample/bin", configuration, "net10.0");
        Assert.True(!OperatingSystem.IsLinux() || File.Exists(Path.Combine(source, "lumyte-cef-supervisor")), "Linux engine-free fixture must stage lumyte-cef-supervisor; build Helper.Sample first.");
        string target = Path.Combine(Path.GetTempPath(), "lumyte-helper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        string executable = Path.Combine(target, "fake-" + mode + extension);
        File.Copy(Path.Combine(source, "Lumyte.UI.WebView.Helper.Sample" + extension), executable);
        return executable;
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready())
        {
            await Task.Delay(10, deadline.Token);
        }
    }
}
