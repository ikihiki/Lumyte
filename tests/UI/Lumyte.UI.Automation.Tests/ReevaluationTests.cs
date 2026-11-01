using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Automation.Tests;

/// <summary>Checks targets are re-resolved at the input boundary, never cached as widget references.</summary>
public sealed class ReevaluationTests
{
    /// <summary>Checks replacement during lease acquisition waits for the new target and never sends to the old one.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task LeaseBoundaryReplacementUsesTheNewTargetAsync()
    {
        int oldClicks = 0;
        int newClicks = 0;
        var first = new Fixture.RuntimeButton { TestId = "save", Width = 20, Height = 20, OnClick = () => oldClicks++ };
        var second = new Fixture.RuntimeButton { TestId = "next", Width = 20, Height = 20, IsVisible = false, OnClick = () => newClicks++ };
        await using var fixture = new Fixture(Stack()[first, second]);
        var host = new InterceptHost(fixture.Host);
        host.AfterAcquire = () =>
        {
            host.AfterAcquire = null;
            first.Change(node =>
            {
                node.TestId = "old";
                node.IsVisible = false;
            });
            second.Change(node =>
            {
                node.TestId = "save";
                node.IsVisible = true;
            });
        };
        var page = new UiAutomationPage(host);
        await page.GetByTestId("save").ClickAsync();
        Assert.Equal(0, oldClicks);
        Assert.Equal(1, newClicks);
        Assert.Equal(3, fixture.Host.Sent.Count);
        Assert.True(fixture.Frames >= 2);
    }

    /// <summary>Checks a page detects host surface replacement across separate operations.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task SurfaceReplacementRequiresANewPageAsync()
    {
        await using var fixture = new Fixture(Panel());
        using var next = new UiSurface(Panel());
        next.SetViewport(new(100, 80));
        var host = new InterceptHost(fixture.Host);
        var page = new UiAutomationPage(host);
        UiAutomationSnapshot before = await page.SnapshotAsync();
        host.OverrideSurface = next;
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => page.SnapshotAsync());
        Assert.Equal(UiAutomationFailureKind.HostUnavailable, error.Kind);
        UiAutomationSnapshot after = await new UiAutomationPage(host).SnapshotAsync();
        Assert.NotEqual(before.SurfaceId, after.SurfaceId);
    }

    /// <summary>Checks Move-induced geometry changes stop the series after Move without retrying the action.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task MoveCallbackChangingGeometryStopsBeforeDownAsync()
    {
        int clicks = 0;
        var root = new Fixture.OffsetPanel();
        var content = new MoveContent { Width = 20, Height = 20, Moved = () => root.Move(new(30, 0)) };
        Definitions.Button button = Button(testId: "save", width: 20, height: 20, onClick: () => clicks++)[content];
        root.Children.Initialize(button);
        await using var fixture = new Fixture(root);
        UiAutomationException failure = await Assert.ThrowsAsync<UiAutomationException>(() => fixture.Page.GetByTestId("save").ClickAsync());
        Assert.Equal(UiAutomationFailureKind.TargetChanged, failure.Kind);
        Assert.Equal(UiPointerPhase.Move, Assert.IsType<UiPointerInput>(Assert.Single(fixture.Host.Sent)).Phase);
        Assert.Equal(0, clicks);
        Assert.False(((ButtonElement)fixture.Element(button)).IsPressed);
    }

    /// <summary>Checks a modifier callback redirecting focus prevents the requested key from reaching another target.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ModifierFocusChangeStopsBeforeTheRequestedKeyAsync()
    {
        var first = new KeyContent { TestId = "first", Width = 20, Height = 20, Focusable = true };
        Definitions.Button other = Button(width: 20, height: 20);
        await using var fixture = new Fixture(Stack(orientation: UiOrientation.Horizontal)[first, other]);
        first.ControlDown = () => fixture.Surface.Dispatch(new UiPointerInput(999, UiPointerPhase.Down, new(25, 5)));
        UiAutomationException failure = await Assert.ThrowsAsync<UiAutomationException>(() => fixture.Page.GetByTestId("first").PressAsync(Lumyte.Input.Abstractions.Key.A, UiModifiers.Control));
        Assert.Equal(UiAutomationFailureKind.TargetChanged, failure.Kind);
        Assert.DoesNotContain(fixture.Host.Sent.OfType<UiKeyInput>(), input => input.Key == Lumyte.Input.Abstractions.Key.A);
        Assert.Same(other, fixture.Surface.FocusedElement?.Widget);
        Assert.True(((ButtonElement)fixture.Element(other)).IsPressed);
        Assert.Contains(fixture.Host.Sent, input => input is UiKeyCancelInput);
        fixture.Surface.Dispatch(new UiPointerInput(999, UiPointerPhase.Cancel, default));
    }

    private sealed class MoveContent : Definitions.Rectangle
    {
        internal Action? Moved { get; init; }

        protected override UiElement CreateElement() => new MoveContentElement(this);
    }

    private sealed class MoveContentElement : RectangleElement
    {
        internal MoveContentElement(MoveContent widget)
            : base(widget)
        {
        }

        protected override UiInputResult OnInput(in UiInputContext context)
        {
            if (context.Input is UiPointerInput { Phase: UiPointerPhase.Move })
            {
                ((MoveContent)Widget).Moved?.Invoke();
            }

            return UiInputResult.Unhandled;
        }
    }

    private sealed class KeyContent : Definitions.Rectangle
    {
        internal Action? ControlDown { get; set; }

        protected override UiElement CreateElement() => new KeyContentElement(this);
    }

    private sealed class KeyContentElement : RectangleElement
    {
        internal KeyContentElement(KeyContent widget)
            : base(widget)
        {
        }

        protected override UiInputResult OnInput(in UiInputContext context)
        {
            if (context.Input is UiKeyInput { Key: Lumyte.Input.Abstractions.Key.LeftControl, IsDown: true })
            {
                ((KeyContent)Widget).ControlDown?.Invoke();
            }

            return UiInputResult.Unhandled;
        }
    }

    private sealed class InterceptHost(IUiAutomationHost inner) : IUiAutomationHost
    {
        public TimeProvider TimeProvider => inner.TimeProvider;

        internal Action? AfterAcquire { get; set; }

        internal UiSurface? OverrideSurface { get; set; }

        public ValueTask<T> RunOnUiThreadAsync<T>(Func<UiSurface, T> action, CancellationToken cancellationToken = default) => inner.RunOnUiThreadAsync(surface => action(OverrideSurface ?? surface), cancellationToken);

        public ValueTask NextFrameAsync(CancellationToken cancellationToken = default) => inner.NextFrameAsync(cancellationToken);

        public async ValueTask<IUiAutomationInputLease> AcquireInputAsync(CancellationToken cancellationToken = default)
        {
            IUiAutomationInputLease lease = await inner.AcquireInputAsync(cancellationToken);
            AfterAcquire?.Invoke();
            return lease;
        }
    }
}
