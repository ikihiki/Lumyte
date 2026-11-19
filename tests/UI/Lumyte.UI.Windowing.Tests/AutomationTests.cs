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

    /// <summary>A combo covered by another normal window cannot open through automation.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ComboAutomationHonorsNormalWindowOcclusionAsync()
    {
        using var f = new Fixture();
        var popupHost = new Lumyte.UI.Controls.Windowing.CollectionPopupHost(f.Popups, f.Owner);
        f.Surface.Rebuild(Lumyte.UI.Collections.Compose.ComboBox<string>(items: new[] { "a" }, keySelector: item => item, itemTemplate: _ => Lumyte.UI.Compose.SizedBox(height: 20), selectedItemTemplate: _ => Lumyte.UI.Compose.SizedBox(height: 20), emptyTemplate: () => Lumyte.UI.Compose.SizedBox(height: 20), popupHost: popupHost, testId: "combo"));
        f.Surface.UpdateLayout();
        using var other = new UiSurface(Lumyte.UI.Compose.Button());
        UiWindowId window = f.Windows.Create(new(f.Registration.Window, new(200, 150), UiWindowRole.Normal, UiWindowInputMode.Interactive, new(f.Registration.Window, new(0, 0, 0, 0), UiWindowPlacementSide.Below)));
        Task<UiWindowBinding> pending = f.Windows.BindPresentationAsync(window, other).AsTask();
        f.Pump();
        UiWindowBinding binding = await pending;
        using UiPopupOwner owner = f.Popups.RegisterOwner(window, other);
        f.Windows.Show(window);
        f.Pump();
        using var gate = new SemaphoreSlim(1, 1);
        IUiAutomationHost Factory(UiSurface surface) => new Host(surface, () => f.Pump(1), gate);
        await using var session = new UiPopupAutomationSession(f.Popups, Factory(f.Surface), Factory);
        var page = new UiAutomationPage(session.CreateOwnerHost(f.Surface));
        await Assert.ThrowsAsync<UiAutomationException>(() => page.GetByTestId("combo").ClickAsync(new() { Timeout = TimeSpan.FromMilliseconds(30) }));
        Assert.Empty(f.Popups.CapturePopups());
        owner.Dispose();
        Task retired = binding.DisposeAsync().AsTask();
        f.Pump();
        Assert.True(retired.IsCompletedSuccessfully);
        f.Windows.Close(window);
        f.Pump();
        f.Windows.Release(window);
        f.Pump();
    }

    /// <summary>Remote actions share the actual modal policy and exclusive group lease, including background rejection.</summary>
    /// <returns>The asynchronous cleanup completion.</returns>
    [Fact]
    public async Task RemoteDialogActionsRejectUnderlyingLocatorAndAllowCurrentDialogAsync()
    {
        using var fixture = new Fixture();
        int dialogClicks = 0;
        Lumyte.UI.Windowing.Compose.Definitions.Dialog portal = Lumyte.UI.Windowing.Compose.Dialog(key: "modal", isOpen: true, initialClientSize: new System.Numerics.Vector2(80, 60))[Lumyte.UI.Compose.Button(testId: "dialog-action", width: 40, height: 20, onClick: () => dialogClicks++)];
        fixture.Surface.Rebuild(Lumyte.UI.Compose.Panel()[Lumyte.UI.Compose.Button(testId: "behind", width: 20, height: 20), portal]);
        fixture.Surface.UpdateLayout();
        using var dialogs = new UiDialogPortalCoordinator(fixture.Windows, fixture.Popups, fixture.Owner, fixture.Registration.Window, fixture.Surface);
        void Pump()
        {
            fixture.Pump(1);
            dialogs.Advance();
        }

        for (int index = 0; index < 24; index++)
        {
            Pump();
        }

        UiDialogOpened opened = Assert.Single(dialogs.CaptureDialogs());
        Assert.True(dialogs.TryGetSurface(opened.DialogId, out UiSurface? content));
        using var input = new SemaphoreSlim(1, 1);
        IUiAutomationHost Factory(UiSurface surface) => new Host(surface, () => fixture.Pump(1), input);
        var routing = new UiPopupAutomationSession(fixture.Popups, Factory(fixture.Surface), Factory);
        using var runtime = new UiAutomationDiagnosticsRuntime(TimeProvider.System, new());
        using UiAutomationDiagnosticRegistration parent = runtime.Register("dialog-owner", fixture.Surface, routing.CreateOwnerHost(fixture.Surface), new() { AllowInput = true });
        using UiAutomationDiagnosticRegistration child = runtime.RegisterPopup(parent, opened.DialogId, "dialog-content", content!, routing.CreateOwnerHost(content!), new() { AllowInput = true });
        var operations = new DiagnosticOperationSet(new UiAutomationDiagnostics(runtime));
        var remoteSession = Guid.NewGuid();
        long frame = 0;
        try
        {
            var control = Guid.Parse(Call("control-acquire", new() { ["target-id"] = DiagnosticValue.From(parent.TargetId.ToString("D")), ["duration-ms"] = DiagnosticValue.From(5000L) })["control-id"].String!);
            Assert.Null(fixture.Popups.ProbePointer(fixture.Surface, new(5)).ActivationTarget);
            Assert.False(fixture.Popups.CanReceiveKeys(fixture.Surface));
            Assert.Equal("TimedOut", Complete(parent.TargetId, "behind", control, 1)["state"].String);
            Assert.Equal(0, dialogClicks);
            Assert.Equal("Succeeded", Complete(child.TargetId, "dialog-action", control, 2)["state"].String);
            Assert.Equal(1, dialogClicks);
        }
        finally
        {
            runtime.BeginStop();
            for (int index = 0; index < 1000 && !runtime.IsStopped; index++)
            {
                runtime.Advance(new(frame++, System.Diagnostics.Stopwatch.GetTimestamp()));
                fixture.Pump(1);
                Thread.Yield();
            }

            Assert.True(runtime.IsStopped);
            await routing.DisposeAsync();
            dialogs.Dispose();
            for (int index = 0; index < 30; index++)
            {
                Pump();
            }

            Assert.True(dialogs.IsStopped);
        }

        IReadOnlyDictionary<string, DiagnosticValue> Call(string operation, Dictionary<string, DiagnosticValue> arguments)
        {
            var context = new DiagnosticOperationContext(Guid.NewGuid(), remoteSession, new(frame, 0), "modal-test", null, CancellationToken.None);
            DiagnosticOperationResult result = operations.Invoke(operation, arguments, context, new HashSet<DiagnosticPermission> { DiagnosticPermission.Observe, DiagnosticPermission.OverrideInput });
            Assert.Equal("success", result.Status);
            return result.Values!;
        }

        IReadOnlyDictionary<string, DiagnosticValue> Complete(Guid target, string testId, Guid control, long sequence)
        {
            IReadOnlyDictionary<string, DiagnosticValue> accepted = Call("action-start", new()
            {
                ["target-id"] = DiagnosticValue.From(target.ToString("D")),
                ["control-id"] = DiagnosticValue.From(control.ToString("D")),
                ["sequence"] = DiagnosticValue.From(sequence),
                ["plan-json"] = DiagnosticValue.From(UiRemoteSchema.SerializePlan(new UiRemotePlan(9, "click", [new("test-id", Value: testId)]))),
                ["job-timeout-ms"] = DiagnosticValue.From(1000L),
            });
            for (int index = 0; index < 2000; index++)
            {
                runtime.Advance(new(frame++, System.Diagnostics.Stopwatch.GetTimestamp()));
                fixture.Pump(1);
                IReadOnlyDictionary<string, DiagnosticValue> state = Call("action-get", new() { ["job-id"] = accepted["job-id"] });
                if (state["state"].String is "Succeeded" or "Failed" or "Canceled" or "TimedOut")
                {
                    return state;
                }

                Thread.Yield();
            }

            throw new InvalidOperationException("The bounded remote modal test did not finish.");
        }
    }

    /// <summary>Offscreen key realization never scrolls a covered owner or a different normal window.</summary>
    /// <param name="covered">Whether another real hosted window covers the group point.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyRealizationHonorsNormalWindowOcclusionAsync(bool covered)
    {
        using var f = new Fixture();
        int templates = 0;
        f.Surface.Rebuild(Lumyte.UI.Compose.Panel()[Collections.Compose.ListBox<int>(
            items: Enumerable.Range(0, 1000).ToArray(),
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ =>
            {
                templates++;
                return Lumyte.UI.Compose.SizedBox(height: 20);
            },
            testId: "list",
            width: 200,
            height: 60,
            virtualization: new Collections.UiItemVirtualization(20, 0))]);
        f.Surface.UpdateLayout();
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(f.Surface.RootElement.Children[0].Children[0]);
        using var other = new UiSurface(Lumyte.UI.Compose.Button());
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

        Assert.Equal(covered ? window : f.Registration.Window, f.Windows.HitTest(f.Registration.Window, new(100, 30))!.Value.Window);
        using var gate = new SemaphoreSlim(1, 1);
        IUiAutomationHost Factory(UiSurface surface) => new Host(surface, () => f.Pump(1), gate);
        await using var session = new UiPopupAutomationSession(f.Popups, Factory(f.Surface), Factory);
        var page = new UiAutomationPage(session.CreateOwnerHost(f.Surface));
        try
        {
            UiLocator item = page.GetByTestId("list").GetByItemKey("999");
            Assert.Equal(0, await item.CountAsync());
            if (covered)
            {
                await Assert.ThrowsAsync<UiAutomationException>(() => item.BringIntoViewAsync(new() { Timeout = TimeSpan.FromMilliseconds(30) }));
                Assert.Equal(0, scroll.Controller.Offset);
                Assert.Equal(3, templates);
            }
            else
            {
                await item.BringIntoViewAsync();
                Assert.Equal(19940, scroll.Controller.Offset);
                Assert.Equal(1, await item.CountAsync());
                Assert.Equal(6, templates);
            }
        }
        finally
        {
            owner.Dispose();
            Task retired = binding.DisposeAsync().AsTask();
            f.Pump();
            Assert.True(retired.IsCompletedSuccessfully);
            f.Windows.Close(window);
            f.Pump();
            f.Windows.Release(window);
            f.Pump();
        }
    }

    /// <summary>Covered real windows cannot discover collapsed tree children through key input.</summary>
    /// <param name="covered">Whether a real window covers the tree.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TreePathRealizationHonorsNormalWindowOcclusionAsync(bool covered)
    {
        using var f = new Fixture();
        int templates = 0;
        f.Surface.Rebuild(Lumyte.UI.Compose.Panel()[Collections.Compose.TreeView<int>(
            roots: new[] { -1 },
            childrenSelector: _ => Enumerable.Range(0, 1000).ToArray(),
            hasChildren: item => item == -1,
            keyPathSelector: key => new[] { "-1", key },
            keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
            itemTemplate: _ =>
            {
                templates++;
                return Lumyte.UI.Compose.SizedBox(height: 20);
            },
            testId: "list",
            width: 200,
            height: 60,
            virtualization: new Collections.UiItemVirtualization(20, 0))]);
        f.Surface.UpdateLayout();
        ScrollViewElement scroll = Assert.IsType<ScrollViewElement>(f.Surface.RootElement.Children[0].Children[0].Children[0]);
        using var other = new UiSurface(Lumyte.UI.Compose.Button());
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

        Assert.Equal(covered ? window : f.Registration.Window, f.Windows.HitTest(f.Registration.Window, new(100, 30))!.Value.Window);
        using var gate = new SemaphoreSlim(1, 1);
        IUiAutomationHost Factory(UiSurface surface) => new Host(surface, () => f.Pump(1), gate);
        await using var session = new UiPopupAutomationSession(f.Popups, Factory(f.Surface), Factory);
        var page = new UiAutomationPage(session.CreateOwnerHost(f.Surface));
        try
        {
            UiLocator item = page.GetByTestId("list").GetByItemKey("999");
            Assert.Equal(0, await item.CountAsync());
            if (covered)
            {
                await Assert.ThrowsAsync<UiAutomationException>(() => item.BringIntoViewAsync(new() { Timeout = TimeSpan.FromMilliseconds(30) }));
                Assert.Equal(0, scroll.Controller.Offset);
                Assert.Equal(1, templates);
            }
            else
            {
                await item.BringIntoViewAsync();
                Assert.Equal(19960, scroll.Controller.Offset);
                Assert.Equal(1, await item.CountAsync());
                Assert.InRange(templates, 4, 8);
            }
        }
        finally
        {
            owner.Dispose();
            Task retired = binding.DisposeAsync().AsTask();
            f.Pump();
            Assert.True(retired.IsCompletedSuccessfully);
            f.Windows.Close(window);
            f.Pump();
            f.Windows.Release(window);
            f.Pump();
        }
    }

    /// <summary>The actual popup page reveals only its own key scope before normal selection.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task VirtualPopupKeySelectionUsesPopupScopeAsync()
    {
        using var f = new Fixture();
        int changes = 0;
        UiPopupHandle popup = f.Popups.RequestOpen(f.Owner, new(f.Anchor, new(80, 60), () => Collections.Compose.ListBox<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: item => item.ToString(System.Globalization.CultureInfo.InvariantCulture), itemTemplate: _ => Lumyte.UI.Compose.SizedBox(height: 20), testId: "list", height: 60, virtualization: new Collections.UiItemVirtualization(20, 0), onSelectionChanged: _ => changes++)));
        f.Pump();
        using var gate = new SemaphoreSlim(1, 1);
        IUiAutomationHost Factory(UiSurface surface) => new Host(surface, () => f.Pump(1), gate);
        await using var session = new UiPopupAutomationSession(f.Popups, Factory(f.Surface), Factory);
        UiAutomationPage page = session.Popup(popup.Id);
        await page.GetByTestId("list").GetByItemKey("999").ClickAsync();
        Assert.Equal(1, changes);
        Assert.Equal(0, f.Clicks);
        popup.Close();
        f.Pump();
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
