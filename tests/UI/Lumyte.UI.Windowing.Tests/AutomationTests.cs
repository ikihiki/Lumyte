using Lumyte.Diagnostics;
using Lumyte.Diagnostics.UI.Automation;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.Input.Abstractions;
using Lumyte.UI.Automation;
using Lumyte.UI.Windowing.Automation;
using Xunit;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Verifies AutomationTests.</summary>
public sealed class AutomationTests
{
    /// <summary>A blocked owner wheel must fail rather than report a consumed menu gate as success.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task OwnerScrollCannotEscapeMenuScopeAsync()
    {
        using var f = new Fixture();
        using var controller = new ScrollController();
        f.Surface.Rebuild(Lumyte.UI.Compose.Panel()[f.Root.Children[0], Lumyte.UI.Compose.ScrollView(controller: controller, testId: "viewport")[Lumyte.UI.Compose.SizedBox(height: 600)]]);
        f.Surface.UpdateLayout();
        f.Open();
        var gate = new SemaphoreSlim(1, 1);
        IUiAutomationHost Factory(UiSurface surface) => new Host(surface, () => f.Pump(1), gate);
        await using var session = new UiPopupAutomationSession(f.Popups, Factory(f.Surface), Factory);
        var page = new UiAutomationPage(session.CreateOwnerHost(f.Surface));
        await Assert.ThrowsAsync<UiAutomationException>(() => page.GetByTestId("viewport").ScrollAsync(new(0, 50)));
        Assert.Equal(0, controller.Offset);
    }

    /// <summary>Checks PopupPageRoutesClickAndEnterThroughMenuScope.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task PopupPageRoutesClickAndEnterThroughMenuScopeAsync()
    {
        using var f = new Fixture();
        int clicks = 0;
        UiPopupHandle popup = f.Open(() => clicks++);
        var gate = new SemaphoreSlim(1, 1);
        IUiAutomationHost Factory(UiSurface surface) => new Host(surface, () => f.Pump(1), gate);
        await using var session = new UiPopupAutomationSession(f.Popups, Factory(f.Surface), Factory);
        UiAutomationPage page = session.Popup(popup.Id);
        await page.GetByTestId("item").ClickAsync();
        await page.GetByTestId("item").PressAsync(Key.Enter);
        Assert.Equal(2, clicks);
        Assert.Equal(0, f.Clicks);
        Assert.Single(await session.ListPopupsAsync());
        popup.Close();
        f.Pump();
        Assert.Empty(await session.ListPopupsAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => session.Popup(Guid.NewGuid()).GetByTestId("missing").CountAsync());
    }

    /// <summary>Checks RootRoutingProbeRefusesBackButtonWhileMenuOwnsInput.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task RootRoutingProbeRefusesBackButtonWhileMenuOwnsInputAsync()
    {
        using var f = new Fixture();
        f.Open();
        var gate = new SemaphoreSlim(1, 1);
        IUiAutomationHost Factory(UiSurface surface) => new Host(surface, () => f.Pump(1), gate);
        await using var session = new UiPopupAutomationSession(f.Popups, Factory(f.Surface), Factory);
        IUiAutomationHost owner = session.CreateOwnerHost(f.Surface);
        IUiAutomationRoutingHost routing = Assert.IsAssignableFrom<IUiAutomationRoutingHost>(owner);
        Assert.Null(routing.ProbePointer(f.Surface, new(5, 5)).ActivationTarget);
        Assert.False(routing.CanReceiveKeys(f.Surface));
        Assert.Equal(0, f.Clicks);
    }

    /// <summary>Routes a desired-state command through the same hosted popup coordinates and scope.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task PopupCheckedInputUsesNormalSurfaceAndIsIdempotentAsync()
    {
        using var fixture = new Fixture();
        int changes = 0;
        UiPopupHandle popup = fixture.Popups.RequestOpen(fixture.Owner, new(
            fixture.Anchor,
            new(80, 60),
            () => Lumyte.UI.Controls.Compose.CheckBox(testId: "check", width: 60, height: 20, threeState: true, onChanged: _ => changes++)));
        fixture.Pump();
        var gate = new SemaphoreSlim(1, 1);
        IUiAutomationHost Factory(UiSurface surface) => new Host(surface, () => fixture.Pump(1), gate);
        await using var session = new UiPopupAutomationSession(fixture.Popups, Factory(fixture.Surface), Factory);
        UiAutomationPage page = session.Popup(popup.Id);
        await page.GetByTestId("check").CheckAsync();
        await page.GetByTestId("check").CheckAsync();
        await page.GetByTestId("check").UncheckAsync();
        Assert.Equal(2, changes);
        Assert.False((await page.SnapshotAsync()).Nodes.Single(node => node.TestId == "check").IsChecked);
    }

    /// <summary>Parent desired input dismisses children like parent pointer Down.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ParentMenuCheckClosesChildAndCommitsOnceAsync()
    {
        using var fixture = new Fixture();
        var changes = new List<bool?>();
        UiPopupHandle parent = fixture.Popups.RequestOpen(fixture.Owner, new(
            fixture.Anchor,
            new(80, 60),
            () => Lumyte.UI.Controls.Compose.CheckBox(testId: "check", width: 60, height: 20, onChanged: changes.Add)));
        fixture.Pump();
        Assert.True(fixture.Popups.TryGetSurface(parent.Id, out UiSurface? parentSurface));
        UiPopupHandle OpenChild()
        {
            UiPopupHandle child = fixture.Popups.RequestOpen(parent, new(
                parentSurface!.RootElement,
                new(60, 40),
                () => Lumyte.UI.Compose.Button(width: 40, height: 20),
                side: UiWindowPlacementSide.Right));
            fixture.Pump();
            Assert.Equal(UiPopupState.Open, child.State);
            return child;
        }

        var gate = new SemaphoreSlim(1, 1);
        IUiAutomationHost Factory(UiSurface surface) => new Host(surface, () => fixture.Pump(1), gate);
        await using var session = new UiPopupAutomationSession(fixture.Popups, Factory(fixture.Surface), Factory);
        UiAutomationPage page = session.Popup(parent.Id);
        UiPopupHandle first = OpenChild();
        await page.GetByTestId("check").CheckAsync();
        fixture.Pump();
        Assert.Equal(UiPopupState.Closed, first.State);
        Assert.True((await page.SnapshotAsync()).Nodes.Single(node => node.TestId == "check").IsChecked);
        UiPopupHandle second = OpenChild();
        await page.GetByTestId("check").UncheckAsync();
        fixture.Pump();
        Assert.Equal(UiPopupState.Closed, second.State);
        Assert.False((await page.SnapshotAsync()).Nodes.Single(node => node.TestId == "check").IsChecked);
        Assert.Equal(new bool?[] { true, false }, changes);
    }

    /// <summary>Remote wheel authority for window A must not scroll an overlapping normal window B.</summary>
    /// <param name="covered">Whether a separate normal window covers A's viewport center.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RemoteScrollHonorsNormalWindowOcclusionAsync(bool covered)
    {
        using var f = new Fixture();
        using var requested = new ScrollController();
        using var covering = new ScrollController();
        f.Surface.Rebuild(Lumyte.UI.Compose.ScrollView(controller: requested, testId: "viewport")[Lumyte.UI.Compose.SizedBox(height: 600)]);
        f.Surface.UpdateLayout();
        using var other = new UiSurface(Lumyte.UI.Compose.ScrollView(controller: covering, testId: "viewport")[Lumyte.UI.Compose.SizedBox(height: 600)]);
        UiWindowId window = f.Windows.Create(new(f.Registration.Window, new(200, 150), UiWindowRole.Normal, UiWindowInputMode.Interactive, new(f.Registration.Window, new(0, 0, 0, 0), UiWindowPlacementSide.Below)));
        Task<UiWindowBinding> pending = f.Windows.BindPresentationAsync(window, other).AsTask();
        f.Pump();
        UiWindowBinding binding = await pending;
        using UiPopupOwner owner = f.Popups.RegisterOwner(window, other);
        if (covered)
        {
            f.Windows.Show(window);
            f.Pump();
        }

        var center = new System.Numerics.Vector2(100, 75);
        Assert.Equal(covered ? window : f.Registration.Window, f.Windows.HitTest(f.Registration.Window, center)!.Value.Window);
        Assert.NotNull(f.Surface.ProbeWheel(center));
        Assert.NotNull(other.ProbeWheel(center));
        Assert.Empty(f.Popups.CapturePopups());
        using var gate = new SemaphoreSlim(1, 1);
        IUiAutomationHost Factory(UiSurface surface) => new Host(surface, () => f.Pump(1), gate);
        await using var session = new UiPopupAutomationSession(f.Popups, Factory(f.Surface), Factory);
        using var runtime = new UiAutomationDiagnosticsRuntime(TimeProvider.System, new());
        using UiAutomationDiagnosticRegistration registration = runtime.Register("window-a", f.Surface, session.CreateOwnerHost(f.Surface), new() { AllowInput = true });
        var operations = new DiagnosticOperationSet(new UiAutomationDiagnostics(runtime));
        var sessionId = Guid.NewGuid();
        long frame = 0;
        IReadOnlyDictionary<string, DiagnosticValue> Call(string operation, Dictionary<string, DiagnosticValue> args)
        {
            var context = new DiagnosticOperationContext(Guid.NewGuid(), sessionId, new(frame, 0), "operator", null, CancellationToken.None);
            DiagnosticOperationResult result = operations.Invoke(operation, args, context, new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe, DiagnosticPermission.OverrideInput });
            Assert.True(result.Status == "success", result.Code + ": " + result.Message);
            return result.Values!;
        }

        try
        {
            string target = registration.TargetId.ToString("D");
            string control = Call("control-acquire", new() { ["target-id"] = DiagnosticValue.From(target), ["duration-ms"] = DiagnosticValue.From(5000L) })["control-id"].String!;
            var plan = new UiRemotePlan(5, "scroll", [new("test-id", "viewport")], Position: new(0, 50));
            string job = Call("action-start", new()
            {
                ["target-id"] = DiagnosticValue.From(target),
                ["control-id"] = DiagnosticValue.From(control),
                ["sequence"] = DiagnosticValue.From(1L),
                ["plan-json"] = DiagnosticValue.From(UiRemoteSchema.SerializePlan(plan)),
                ["job-timeout-ms"] = DiagnosticValue.From(1000L),
            })["job-id"].String!;
            Assert.Equal(0, requested.Offset);
            Assert.Equal(0, covering.Offset);
            IReadOnlyDictionary<string, DiagnosticValue>? terminal = null;
            for (int i = 0; i < 1000; i++)
            {
                runtime.Advance(new(frame++, long.MaxValue));
                f.Pump(1);
                IReadOnlyDictionary<string, DiagnosticValue> result = Call("action-get", new() { ["job-id"] = DiagnosticValue.From(job) });
                if (result["state"].String is "Succeeded" or "Failed" or "Canceled" or "TimedOut")
                {
                    terminal = result;
                    break;
                }

                Thread.Yield();
            }

            Assert.NotNull(terminal);
            Assert.Equal(covered ? "Failed" : "Succeeded", terminal["state"].String);
            if (covered)
            {
                Assert.Equal("target-changed", terminal["code"].String);
            }

            Assert.Equal(covered ? 0 : 50, requested.Offset);
            Assert.Equal(0, covering.Offset);
        }
        finally
        {
            runtime.BeginStop();
            for (int i = 0; i < 1000 && !runtime.IsStopped; i++)
            {
                runtime.Advance(new(frame++, long.MaxValue));
                f.Pump(1);
                Thread.Yield();
            }

            Assert.True(runtime.IsStopped);
            owner.Dispose();
            Task retired = binding.DisposeAsync().AsTask();
            f.Pump();
            Assert.True(retired.IsCompletedSuccessfully);
            f.Windows.Close(window);
            f.Pump();
            f.Windows.Release(window);
        }
    }

    private sealed class Host(UiSurface surface, Action nextFrame, SemaphoreSlim gate) : IUiAutomationHost
    {
        private static long _next;

        public TimeProvider TimeProvider => TimeProvider.System;

        public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(action(surface));
        }

        public ValueTask NextFrameAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            nextFrame();
            return ValueTask.CompletedTask;
        }

        public async ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken token = default)
        {
            await gate.WaitAsync(token);
            return new Lease(gate, Interlocked.Increment(ref _next));
        }

        private sealed class Lease(SemaphoreSlim gate, long id) : IUiAutomationInputLease
        {
            private bool _disposed;

            public long PointerId => id;

            public bool Dispatch(UiSurface target, UiInput input) => target.Dispatch(input);

            public ValueTask DisposeAsync()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    gate.Release();
                }

                return ValueTask.CompletedTask;
            }
        }
    }
}
