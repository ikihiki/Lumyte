using Lumyte.Input.Abstractions;
using Lumyte.UI.Automation;
using Lumyte.UI.Windowing.Automation;
using Xunit;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Verifies AutomationTests.</summary>
public sealed class AutomationTests
{
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
