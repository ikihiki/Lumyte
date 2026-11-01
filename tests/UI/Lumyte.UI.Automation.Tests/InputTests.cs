using System.Numerics;
using Lumyte.Input.Abstractions;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Automation.Tests;

/// <summary>Checks normal pointer/key series, actionability and exception cleanup without callback retries.</summary>
public sealed class InputTests
{
    /// <summary>Checks one stable click emits ordinary input in order and invokes the callback once.</summary>
    /// <param name="hasCallback">Whether a callback exists.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClickUsesExactlyOneInputSeriesAsync(bool hasCallback)
    {
        int clicks = 0;
        Definitions.Button button = Button(testId: "save", width: 20, height: 20, onClick: hasCallback ? () => clicks++ : null)[Rectangle(width: 20, height: 20)];
        await using var fixture = new Fixture(Panel()[button]);
        await fixture.Page.GetByTestId("save").ClickAsync();
        Assert.Equal(hasCallback ? 1 : 0, clicks);
        Assert.False(((ButtonElement)fixture.Element(button)).IsPressed);
        Assert.Equal(1, fixture.Frames);
        Assert.Equal(new[] { UiPointerPhase.Move, UiPointerPhase.Down, UiPointerPhase.Up }, fixture.Host.Sent.OfType<UiPointerInput>().Select(input => input.Phase));
        UiAutomationNode node = (await fixture.Page.SnapshotAsync()).Nodes.Single(node => node.TestId == "save");
        Assert.True(node.IsFocused);
        Assert.False(node.HasActivePress);
    }

    /// <summary>Checks action capabilities are not inferred from a role.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task RoleSpoofingFailsBeforeInputAsync()
    {
        await using var fixture = new Fixture(Rectangle(role: UiRole.Button, testId: "fake"));
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => fixture.Page.GetByTestId("fake").ClickAsync());
        Assert.Equal(UiAutomationFailureKind.UnsupportedAction, error.Kind);
        Assert.Empty(fixture.Host.Sent);
    }

    /// <summary>Checks explicit position can avoid an occluded center without automatic point searching.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task OcclusionWaitsButExplicitVisibleOffsetCanClickAsync()
    {
        int clicks = 0;
        var cover = new Fixture.OffsetPanel { Offset = new(8, 8), Width = 5, Height = 5, HitTestVisible = true };
        cover.Children.Initialize(Rectangle(width: 5, height: 5));
        await using var fixture = new Fixture(Panel()[Button(testId: "save", width: 20, height: 20, onClick: () => clicks++), cover]);
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => fixture.Page.GetByTestId("save").ClickAsync(new() { Timeout = TimeSpan.FromMilliseconds(40) }));
        Assert.Equal(UiAutomationFailureKind.Timeout, error.Kind);
        Assert.Equal(0, clicks);
        Assert.Empty(fixture.Host.Sent);
        await fixture.Page.GetByTestId("save").ClickAsync(new() { Position = new(18, 18) });
        Assert.Equal(1, clicks);
    }

    /// <summary>Checks runtime eligibility changes are awaited before any pointer series is sent.</summary>
    /// <param name="state">Hidden, disabled or excluded from hit testing.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ClickWaitsForRuntimeEligibilityAsync(int state)
    {
        var button = new Fixture.RuntimeButton { TestId = "save", Width = 20, Height = 20, IsVisible = state != 0, IsEnabled = state != 1, HitTestVisible = state != 2 };
        await using var fixture = new Fixture(Panel()[button]);
        fixture.OnFrame = () =>
        {
            Assert.Empty(fixture.Host.Sent);
            if (fixture.Frames == 2)
            {
                button.Change(node =>
                {
                    node.IsVisible = true;
                    node.IsEnabled = true;
                    node.HitTestVisible = true;
                });
            }
        };
        await fixture.Page.GetByTestId("save").ClickAsync();
        Assert.True(fixture.Frames >= 2);
        Assert.Equal(3, fixture.Host.Sent.Count);
    }

    /// <summary>Checks geometry must match on consecutive frames before pointer Down.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task MovingGeometryWaitsForTwoStableFramesAsync()
    {
        var moving = new Fixture.OffsetPanel();
        moving.Children.Initialize(Button(testId: "save", width: 10, height: 10));
        await using var fixture = new Fixture(moving);
        fixture.OnFrame = () =>
        {
            if (fixture.Frames <= 3)
            {
                moving.Move(new(fixture.Frames * 5, 0));
            }
        };
        await fixture.Page.GetByTestId("save").ClickAsync();
        Assert.Equal(4, fixture.Frames);
        Assert.Equal(new Vector2(20, 5), fixture.Host.Sent.OfType<UiPointerInput>().First().Position);
    }

    /// <summary>Checks an outside captured pointer blocks automation even when IsPressed is false.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ExistingOutsidePressIsNotStolenAsync()
    {
        Definitions.Button button = Button(testId: "save", width: 10, height: 10);
        await using var fixture = new Fixture(Panel()[button]);
        fixture.Surface.Dispatch(new UiPointerInput(99, UiPointerPhase.Down, new(5, 5)));
        fixture.Surface.Dispatch(new UiPointerInput(99, UiPointerPhase.Move, new(90, 70)));
        Assert.False(((ButtonElement)fixture.Element(button)).IsPressed);
        Assert.True(fixture.Surface.CaptureAutomationSnapshot().Nodes[1].HasActivePress);
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => fixture.Page.GetByTestId("save").ClickAsync(new() { Timeout = TimeSpan.FromMilliseconds(30) }));
        Assert.Equal(UiAutomationFailureKind.Timeout, error.Kind);
        Assert.Empty(fixture.Host.Sent);
        fixture.Surface.Dispatch(new UiPointerInput(99, UiPointerPhase.Cancel, default));
        await fixture.Page.GetByTestId("save").ClickAsync();
    }

    /// <summary>Checks Tab focus works even for zero-size focus candidates and Enter uses ordinary key handling.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task FocusAndPressUseTabThenEnterAsync()
    {
        int clicks = 0;
        await using var fixture = new Fixture(Stack()[Button(testId: "first", width: 10, height: 10), Button(testId: "save", width: 0, height: 0, onClick: () => clicks++)]);
        UiLocator save = fixture.Page.GetByTestId("save");
        await save.FocusAsync();
        await fixture.Page.Expect(save).ToBeFocusedAsync();
        Assert.Equal(4, fixture.Host.Sent.Count);
        await save.PressAsync(Key.Enter);
        Assert.Equal(1, clicks);
        Assert.Equal(new[] { Key.Tab, Key.Tab, Key.Tab, Key.Tab, Key.Enter, Key.Enter }, fixture.Host.Sent.OfType<UiKeyInput>().Select(input => input.Key));
        Assert.All(fixture.Host.Sent.OfType<UiKeyInput>(), input => Assert.False(input.IsRepeat));
    }

    /// <summary>Checks physical modifier transitions match declared state and are released in reverse order.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task PressSendsPhysicalModifiersAsync()
    {
        await using var fixture = new Fixture(Button(testId: "save"));
        await fixture.Page.GetByTestId("save").PressAsync(Key.A, UiModifiers.Control | UiModifiers.Shift);
        UiKeyInput[] inputs = fixture.Host.Sent.OfType<UiKeyInput>().Skip(2).ToArray();
        Assert.Equal(new[] { Key.LeftControl, Key.LeftShift, Key.A, Key.A, Key.LeftShift, Key.LeftControl }, inputs.Select(input => input.Key));
        Assert.Equal(new[] { true, true, true, false, false, false }, inputs.Select(input => input.IsDown));
        Assert.Equal(UiModifiers.Control | UiModifiers.Shift, inputs[2].Modifiers);
        Assert.Equal(UiModifiers.None, inputs[^1].Modifiers);
    }

    /// <summary>Checks callback failure is retained as the cause and never triggers automatic click retries.</summary>
    /// <param name="keyboard">Whether to activate with Enter rather than pointer.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackFailureIsNeverRetriedAsync(bool keyboard)
    {
        int clicks = 0;
        var failure = new InvalidOperationException("application callback failed");
        Definitions.Button button = Button(testId: "save", width: 20, height: 20, onClick: Fail);
        void Fail()
        {
            clicks++;
            throw failure;
        }

        await using var fixture = new Fixture(Panel()[button]);
        UiLocator save = fixture.Page.GetByTestId("save");
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => keyboard ? save.PressAsync(Key.Enter) : save.ClickAsync());
        Assert.Equal(UiAutomationFailureKind.ActionFailed, error.Kind);
        Assert.Same(failure, error.InnerException);
        Assert.Equal(1, clicks);
        Assert.False(((ButtonElement)fixture.Element(button)).IsPressed);
        Assert.False(fixture.Surface.CaptureAutomationSnapshot().Nodes[1].HasActivePress);
        Assert.Contains(error.Trace, line => line.Contains("send ", StringComparison.Ordinal));
        Assert.Contains(error.Trace, line => line.Contains("cleaned", StringComparison.Ordinal));
        await using IUiAutomationInputLease lease = await fixture.Host.AcquireInputAsync();
    }

    /// <summary>Checks cancelling a partial Enter lease cancels the key before Up and never clicks.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task LeaseCleanupCancelsOnlyItsOwnInputAsync()
    {
        int clicks = 0;
        Definitions.Button button = Button(testId: "save", width: 20, height: 20, onClick: () => clicks++);
        await using var fixture = new Fixture(Panel()[button]);
        await fixture.Page.GetByTestId("save").FocusAsync();
        IUiAutomationInputLease lease = await fixture.Host.AcquireInputAsync();
        lease.Dispatch(fixture.Surface, new UiKeyInput(Key.Enter, true));
        Assert.True(((ButtonElement)fixture.Element(button)).IsPressed);
        await lease.DisposeAsync();
        await lease.DisposeAsync();
        Assert.Equal(0, clicks);
        Assert.False(((ButtonElement)fixture.Element(button)).IsPressed);
        Assert.Same(button, fixture.Surface.FocusedElement?.Widget);
        Assert.IsType<UiKeyCancelInput>(fixture.Host.Sent[^2]);
        Assert.IsType<UiKeyInput>(fixture.Host.Sent[^1]);
    }

    /// <summary>Checks host shutdown cleans the active lease and rejects new reservations without owning the surface.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ShutdownCleansLeaseAndPreservesBorrowedSurfaceAsync()
    {
        Definitions.Button button = Button(width: 20, height: 20);
        await using var fixture = new Fixture(Panel()[button]);
        IUiAutomationInputLease lease = await fixture.Host.AcquireInputAsync();
        lease.Dispatch(fixture.Surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Down, new(5, 5)));
        await fixture.Host.DisposeAsync();
        Assert.False(((ButtonElement)fixture.Element(button)).IsPressed);
        Assert.NotNull(fixture.Surface.Root);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await fixture.Host.AcquireInputAsync());
        UiAutomationException error = await Assert.ThrowsAsync<UiAutomationException>(() => fixture.Page.SnapshotAsync());
        Assert.Equal(UiAutomationFailureKind.HostUnavailable, error.Kind);
    }
}
