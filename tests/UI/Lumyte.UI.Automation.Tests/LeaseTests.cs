using Lumyte.Input.Abstractions;
using Lumyte.UI.SampleSupport;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Automation.Tests;

/// <summary>Checks deadline sharing, independent pages and input lease cleanup at failure boundaries.</summary>
public sealed class LeaseTests
{
    /// <summary>Checks lease acquisition shares the original deadline rather than extending it.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task LeaseWaitUsesTheSameVirtualDeadlineAsync()
    {
        await using var fixture = new Fixture(Button(testId: "save"));
        await using IUiAutomationInputLease held = await fixture.Host.AcquireInputAsync();
        Task press = fixture.Page.GetByTestId("save").PressAsync(Key.Enter, options: new() { Timeout = TimeSpan.FromMilliseconds(50) });
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(50));
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => press);
        Assert.Equal(UiAutomationFailureKind.Timeout, error.Kind);
        Assert.Empty(fixture.Host.Sent);
    }

    /// <summary>Checks separate pages serialize input on a host and caller cancellation does not return somebody else's lease.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task WaitingPageCannotCancelAnotherPagesLeaseAsync()
    {
        await using var fixture = new Fixture(Button(testId: "save"));
        IUiAutomationInputLease held = await fixture.Host.AcquireInputAsync();
        var second = new UiAutomationPage(fixture.Host);
        using var cancel = new CancellationTokenSource();
        Task pending = second.GetByTestId("save").FocusAsync(cancellationToken: cancel.Token);
        Assert.False(pending.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.NotNull(await fixture.Page.SnapshotAsync());
        await held.DisposeAsync();
        await second.GetByTestId("save").FocusAsync();
    }

    /// <summary>Checks cancellation during a callback completes the atomic input series once and keeps sent input diagnostics.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task CallbackCancellationDoesNotInterruptOrRepeatTheClickAsync()
    {
        using var cancel = new CancellationTokenSource();
        int clicks = 0;
        Definitions.Button button = Button(testId: "save", width: 20, height: 20, onClick: Click);
        void Click()
        {
            clicks++;
            cancel.Cancel();
        }

        await using var fixture = new Fixture(Panel()[button]);
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Page.GetByTestId("save").ClickAsync(cancellationToken: cancel.Token));
        Assert.Equal(1, clicks);
        Assert.False(button.IsPressed);
        Assert.NotNull(error.Data["Lumyte.UI.Automation.Trace"]);
        Assert.Equal(3, fixture.Host.Sent.OfType<UiPointerInput>().Count());
        await using IUiAutomationInputLease next = await fixture.Host.AcquireInputAsync();
    }

    /// <summary>Checks cleanup cancels only its own pointer and retains another captured series.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task PointerCleanupDoesNotResetOtherInputAsync()
    {
        Definitions.Button first = Button(width: 20, height: 20);
        Definitions.Button other = Button(width: 20, height: 20);
        await using var fixture = new Fixture(Stack(orientation: UiOrientation.Horizontal)[first, other]);
        fixture.Surface.Dispatch(new UiPointerInput(900, UiPointerPhase.Down, new(25, 5)));
        IUiAutomationInputLease lease = await fixture.Host.AcquireInputAsync();
        lease.Dispatch(fixture.Surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Down, new(5, 5)));
        await lease.DisposeAsync();
        Assert.False(first.IsPressed);
        Assert.True(other.IsPressed);
        fixture.Surface.Dispatch(new UiPointerInput(900, UiPointerPhase.Cancel, default));
    }

    /// <summary>Checks a lease can return its host reservation after the borrowed surface has ended.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task DisposedSurfaceStillReturnsInputReservationAsync()
    {
        await using var fixture = new Fixture(Button(width: 20, height: 20));
        IUiAutomationInputLease lease = await fixture.Host.AcquireInputAsync();
        lease.Dispatch(fixture.Surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Down, new(5, 5)));
        fixture.Surface.Dispose();
        await lease.DisposeAsync();
        await using IUiAutomationInputLease next = await fixture.Host.AcquireInputAsync();
        Assert.NotEqual(lease.PointerId, next.PointerId);
    }

    /// <summary>Checks invalid positions and wait options fail before sending input.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task InvalidOptionsDoNotSendInputAsync()
    {
        await using var fixture = new Fixture(Button(testId: "save"));
        UiLocator save = fixture.Page.GetByTestId("save");
        await Assert.ThrowsAsync<ArgumentException>(() => save.ClickAsync(new() { Position = new(float.NaN, 0) }));
        await Assert.ThrowsAsync<ArgumentException>(() => save.ClickAsync(new() { Timeout = Timeout.InfiniteTimeSpan }));
        Assert.Empty(fixture.Host.Sent);
    }

    /// <summary>Checks screenshot bytes pass through the optional host contract without claiming a CPU snapshot is a screenshot.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task OptionalScreenshotDelegatesToHostAsync()
    {
        await using var fixture = new Fixture(Panel());
        byte[] image = [1, 2, 3];
        var host = new UiSampleScreenshotHost(fixture.Host, _ => ValueTask.FromResult(image));
        var page = new UiAutomationPage(host);
        Assert.Same(image, await page.ScreenshotAsync());
    }

    /// <summary>Checks an injected terminal delivery failure cancels an active press before Up and never activates it.</summary>
    /// <param name="keyboard">Whether to fail Enter Up rather than pointer Up.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TerminalFailureCancelsPressWithoutActivationAsync(bool keyboard)
    {
        int clicks = 0;
        Definitions.Button button = Button(testId: "save", width: 20, height: 20, onClick: () => clicks++);
        await using var fixture = new Fixture(Panel()[button]);
        var failure = new InvalidOperationException("terminal delivery failed");
        var page = new UiAutomationPage(new FailureHost(fixture.Host, keyboard, failure));
        UiLocator save = page.GetByTestId("save");
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => keyboard ? save.PressAsync(Key.Enter) : save.ClickAsync());
        Assert.Equal(UiAutomationFailureKind.ActionFailed, error.Kind);
        Assert.Same(failure, error.InnerException);
        Assert.Equal(0, clicks);
        Assert.False(button.IsPressed);
        Assert.False(fixture.Surface.CaptureAutomationSnapshot().Nodes[1].HasActivePress);
        Assert.Contains(fixture.Host.Sent, input => keyboard ? input is UiKeyCancelInput : input is UiPointerInput { Phase: UiPointerPhase.Cancel });
        await using IUiAutomationInputLease next = await fixture.Host.AcquireInputAsync();
    }

    private sealed class FailureHost(IUiAutomationHost host, bool keyboard, Exception failure) : IUiAutomationHost
    {
        public TimeProvider TimeProvider => host.TimeProvider;

        public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => host.RunOnUiThreadAsync(action, cancellationToken);

        public ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => host.NextFrameAsync(cancellationToken);

        public async ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default) => new FailureLease(await host.AcquireInputAsync(cancellationToken), keyboard, failure);
    }

    private sealed class FailureLease(IUiAutomationInputLease inner, bool keyboard, Exception failure) : IUiAutomationInputLease
    {
        public long PointerId => inner.PointerId;

        public ValueTask DisposeAsync() => inner.DisposeAsync();

        public bool Dispatch(UiSurface surface, UiInput input)
        {
            if (keyboard ? input is UiKeyInput { Key: Key.Enter, IsDown: false } : input is UiPointerInput { Phase: UiPointerPhase.Up })
            {
                throw failure;
            }

            return inner.Dispatch(surface, input);
        }
    }
}
