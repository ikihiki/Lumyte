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
