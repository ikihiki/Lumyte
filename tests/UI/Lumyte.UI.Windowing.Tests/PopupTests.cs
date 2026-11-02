using System.Numerics;
using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Windowing.Tests.TestTasks;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Verifies PopupTests.</summary>
public sealed class PopupTests
{
    /// <summary>Checks declarative signal opening, portal isolation, controlled dismissal and continuing session identity.</summary>
    /// <param name="presentation">The hosted or native-preferring fallback policy.</param>
    [Theory]
    [InlineData(PopupPresentation.Hosted)]
    [InlineData(PopupPresentation.Automatic)]
    public void DeclarativePopupsObserveModelsAndKeepContentOnTheirOwnSurface(PopupPresentation presentation)
    {
        using var fixture = new Fixture();
        var open = Signal.Create(false);
        int dismissals = 0;
        Definitions.Panel Build()
        {
            Lumyte.UI.Windowing.Compose.Definitions.Popup declaration = Lumyte.UI.Windowing.Compose.Popup(key: "menu", anchor: "anchor", isOpen: open, presentation: presentation, onDismiss: _ => dismissals++)[Button(width: 80, height: 40, testId: "portal-item")];
            return Panel()[Button(key: "anchor", width: 20, height: 20), declaration];
        }

        fixture.Surface.Rebuild(Build());
        fixture.Surface.UpdateLayout();
        using var portals = new UiPopupPortalCoordinator(fixture.Popups, fixture.Owner, fixture.Surface);
        portals.Advance();
        Assert.Empty(fixture.Popups.CapturePopups());
        Assert.DoesNotContain(fixture.Surface.CaptureAutomationSnapshot().Nodes, node => node.TestId == "portal-item");
        open.Value = true;
        fixture.Surface.UpdateLayout();
        portals.Advance();
        fixture.Pump();
        UiPopupInfo first = Assert.Single(fixture.Popups.CapturePopups());
        Assert.Equal(UiPopupState.Open, first.State);
        Assert.True(fixture.Popups.TryGetSurface(first.Id, out UiSurface? content));
        Assert.Contains(content!.CaptureAutomationSnapshot().Nodes, node => node.TestId == "portal-item");
        fixture.Surface.Rebuild(Build());
        fixture.Surface.UpdateLayout();
        portals.Advance();
        fixture.Pump();
        Assert.Equal(first.Id, Assert.Single(fixture.Popups.CapturePopups()).Id);
        fixture.Send(new UiKeyInput(Key.Escape, true));
        fixture.Pump();
        Assert.Equal(1, dismissals);
        Assert.True(open.Value);
        Assert.Equal(UiPopupState.Open, Assert.Single(fixture.Popups.CapturePopups()).State);
        open.Value = false;
        fixture.Surface.UpdateLayout();
        portals.Advance();
        fixture.Pump();
        Assert.Empty(fixture.Popups.CapturePopups());
    }

    /// <summary>Checks HostedMenuCreatesIndependentSurfaceAndRestoresFocus.</summary>
    [Fact]
    public void HostedMenuCreatesIndependentSurfaceAndRestoresFocus()
    {
        using var f = new Fixture();
        f.Surface.MoveFocus(UiFocusNavigation.First);
        UiFocusBookmark bookmark = f.Surface.CaptureFocus();
        UiPopupHandle popup = f.Open();
        Assert.Equal(UiPopupState.Open, popup.State);
        Assert.Equal(UiWindowPresentation.Hosted, popup.Presentation);
        Assert.Null(f.Surface.FocusedElement);
        Assert.True(f.Popups.TryGetSurface(popup.Id, out UiSurface? menu));
        Assert.NotNull(menu!.FocusedElement);
        Assert.Equal(f.Registration.Window, f.Windows.GetKeyboardTarget(f.Registration.Window));
        popup.Close();
        f.Pump();
        Assert.Equal(UiPopupState.Closed, popup.State);
        Assert.Same(f.Anchor, f.Surface.FocusedElement);
        Assert.True(f.Surface.TryRestoreFocus(bookmark));
    }

    /// <summary>Checks OutsidePressConsumesWholeSeriesAndNextPressWorks.</summary>
    [Fact]
    public void OutsidePressConsumesWholeSeriesAndNextPressWorks()
    {
        using var f = new Fixture();
        UiPopupHandle popup = f.Open();
        Assert.True(f.Send(new UiPointerInput(1, UiPointerPhase.Down, new(5, 5))));
        f.Pump();
        Assert.Equal(UiPopupState.Closed, popup.State);
        Assert.True(f.Send(new UiPointerInput(1, UiPointerPhase.Up, new(5, 5))));
        Assert.Equal(0, f.Clicks);
        f.Send(new UiPointerInput(1, UiPointerPhase.Down, new(5, 5)));
        f.Send(new UiPointerInput(1, UiPointerPhase.Up, new(5, 5)));
        Assert.Equal(1, f.Clicks);
    }

    /// <summary>Checks MenuClickUsesNormalButtonInput.</summary>
    [Fact]
    public void MenuClickUsesNormalButtonInput()
    {
        using var f = new Fixture();
        int clicks = 0;
        f.Open(() => clicks++);
        f.Send(new UiPointerInput(7, UiPointerPhase.Down, new(5, 25)));
        f.Send(new UiPointerInput(7, UiPointerPhase.Up, new(5, 25)));
        Assert.Equal(1, clicks);
        Assert.Equal(0, f.Clicks);
    }

    /// <summary>Checks PassivePopupNeverTakesFocusOrBlocksPointer.</summary>
    [Fact]
    public void PassivePopupNeverTakesFocusOrBlocksPointer()
    {
        using var f = new Fixture();
        f.Surface.MoveFocus(UiFocusNavigation.First);
        UiPopupHandle popup = f.Open(mode: UiPopupInputMode.Passive);
        Assert.Same(f.Anchor, f.Surface.FocusedElement);
        f.Send(new UiPointerInput(1, UiPointerPhase.Down, new(5, 5)));
        f.Send(new UiPointerInput(1, UiPointerPhase.Up, new(5, 5)));
        Assert.Equal(1, f.Clicks);
        Assert.Equal(UiPopupState.Open, popup.State);
    }

    /// <summary>Checks EscapeClosesDeepestAndItsUpCannotReachOwner.</summary>
    [Fact]
    public void EscapeClosesDeepestAndItsUpCannotReachOwner()
    {
        using var f = new Fixture();
        UiPopupHandle popup = f.Open();
        Assert.True(f.Send(new UiKeyInput(Key.Escape, true)));
        f.Pump();
        Assert.Equal(UiPopupState.Closed, popup.State);
        Assert.True(f.Send(new UiKeyInput(Key.Escape, false)));
        Assert.Equal(0, f.Clicks);
    }

    /// <summary>Checks ArrowNavigationUsesFocusCandidatesAndEnterUsesButton.</summary>
    [Fact]
    public void ArrowNavigationUsesFocusCandidatesAndEnterUsesButton()
    {
        using var f = new Fixture();
        int clicks = 0;
        UiPopupHandle popup = f.Open(() => clicks++);
        Assert.True(f.Popups.TryGetSurface(popup.Id, out UiSurface? surface));
        UiElement? first = surface!.FocusedElement;
        f.Send(new UiKeyInput(Key.ArrowDown, true));
        f.Send(new UiKeyInput(Key.ArrowDown, false));
        Assert.NotSame(first, surface.FocusedElement);
        f.Send(new UiKeyInput(Key.ArrowUp, true));
        f.Send(new UiKeyInput(Key.ArrowUp, false));
        f.Send(new UiKeyInput(Key.Enter, true));
        f.Send(new UiKeyInput(Key.Enter, false));
        Assert.Equal(1, clicks);
    }

    /// <summary>Checks CausePointerUpIsNeverMovedIntoNewMenu.</summary>
    [Fact]
    public void CausePointerUpIsNeverMovedIntoNewMenu()
    {
        using var f = new Fixture();
        f.Send(new UiPointerInput(1, UiPointerPhase.Down, new(5, 5)));
        int clicks = 0;
        f.Open(() => clicks++);
        f.Send(new UiPointerInput(1, UiPointerPhase.Up, new(5, 25)));
        Assert.Equal(0, clicks);
        Assert.Equal(0, f.Clicks);
    }

    /// <summary>Checks ReplacementWaitsForRetirementAndFactoryRunsOnce.</summary>
    [Fact]
    public void ReplacementWaitsForRetirementAndFactoryRunsOnce()
    {
        using var f = new Fixture();
        int factories = 0;
        UiPopupHandle first = f.Open();
        var gate = new TaskCompletionSource();
        f.Source.RetireGate = gate;
        UiPopupHandle next = f.Popups.RequestOpen(f.Owner, new(f.Anchor, new(50, 30), () =>
        {
            factories++;
            return Button();
        }));
        f.Pump();
        Assert.Equal(UiPopupState.Closing, first.State);
        Assert.Equal(UiPopupState.Pending, next.State);
        Assert.Equal(0, factories);
        gate.SetResult();
        f.Source.RetireGate = null;
        f.Pump();
        Assert.Equal(UiPopupState.Closed, first.State);
        Assert.Equal(UiPopupState.Open, next.State);
        Assert.Equal(1, factories);
    }

    /// <summary>Checks ClosingWaitsForLifetimeParticipantsBeforeDrawing.</summary>
    [Fact]
    public void ClosingWaitsForLifetimeParticipantsBeforeDrawing()
    {
        using var f = new Fixture();
        UiPopupHandle popup = f.Open();
        var participant = new Participant();
        f.Popups.BindLifetime(popup.Id, participant);
        popup.Close();
        f.Pump();
        Assert.Equal(1, participant.Calls);
        Assert.Equal(UiPopupState.Closing, popup.State);
        Assert.False(f.Source.Presentations[1].Disposed);
        Assert.True(f.Send(new UiPointerInput(9, UiPointerPhase.Down, new(5, 5))));
        Assert.True(f.Send(new UiPointerInput(9, UiPointerPhase.Up, new(5, 5))));
        Assert.Equal(0, f.Clicks);
        participant.Gate.SetResult();
        f.Pump();
        Assert.Equal(UiPopupState.Closed, popup.State);
        Assert.True(f.Source.Presentations[1].Disposed);
    }

    /// <summary>Checks InvalidContentFailsAndNotifiesOnce.</summary>
    [Fact]
    public void InvalidContentFailsAndNotifiesOnce()
    {
        using var f = new Fixture();
        int factories = 0, closed = 0;
        Definitions.Widget InvalidContent()
        {
            factories++;
            return Panel()[Button(key: "duplicate"), Button(key: "duplicate")];
        }

        UiPopupHandle popup = f.Popups.RequestOpen(f.Owner, new(f.Anchor, new(30, 30), InvalidContent, onClosed: _ => closed++));
        f.Pump();
        Assert.Equal(UiPopupState.Failed, popup.State);
        Assert.Equal("invalid-content", popup.FailureCode);
        Assert.Equal(1, factories);
        Assert.Equal(1, closed);
    }

    /// <summary>Checks CallbacksMayQueueRequestsButCannotReenterAdvance.</summary>
    [Fact]
    public void CallbacksMayQueueRequestsButCannotReenterAdvance()
    {
        using var f = new Fixture();
        int opened = 0, closed = 0;
        void OnOpened(UiPopupOpened result)
        {
            opened++;
            Assert.Throws<InvalidOperationException>(() => f.Popups.Advance());
            f.Popups.NotifyOutsidePress(f.Owner);
        }

        UiPopupHandle popup = f.Open(opened: OnOpened, closed: _ => closed++);
        f.Pump();
        Assert.Equal(UiPopupState.Closed, popup.State);
        Assert.Equal(1, opened);
        Assert.Equal(1, closed);
    }

    /// <summary>Checks SubmenuClosesBeforeParentAndLeftRestoresParentFocus.</summary>
    [Fact]
    public void SubmenuClosesBeforeParentAndLeftRestoresParentFocus()
    {
        using var f = new Fixture();
        var order = new List<string>();
        UiPopupHandle root = f.Open(closed: _ => order.Add("root"));
        f.Popups.TryGetSurface(root.Id, out UiSurface? surface);
        UiPopupHandle child = f.Popups.RequestOpen(root, new(surface!.FocusedElement!, new(50, 30), () => Button(width: 40, height: 20), side: UiWindowPlacementSide.Right, onClosed: _ => order.Add("child")));
        f.Pump();
        Assert.Equal(UiPopupState.Open, child.State);
        f.Send(new UiKeyInput(Key.ArrowLeft, true));
        f.Pump();
        Assert.Equal(UiPopupState.Closed, child.State);
        Assert.NotNull(surface.FocusedElement);
        f.Send(new UiKeyInput(Key.ArrowLeft, false));
        root.Close();
        f.Pump();
        Assert.Equal(new[] { "child", "root", }, order);
    }

    /// <summary>Checks OwnerDeactivationClosesMenuWithoutRestoringFocus.</summary>
    [Fact]
    public void OwnerDeactivationClosesMenuWithoutRestoringFocus()
    {
        using var f = new Fixture();
        UiPopupHandle popup = f.Open();
        f.Source.Active = false;
        f.Pump();
        Assert.Equal(UiPopupState.Closed, popup.State);
        Assert.Null(f.Surface.FocusedElement);
        f.Source.Active = true;
    }

    /// <summary>Checks pending menu input is consumed before any content or owner callback.</summary>
    [Fact]
    public void PendingMenuConsumesNewKeyboardSeries()
    {
        using var f = new Fixture();
        f.Surface.MoveFocus(UiFocusNavigation.First);
        f.Popups.RequestOpen(f.Owner, new(f.Anchor, new(40, 30), () => Button()));
        f.Popups.Advance();
        Assert.True(f.Send(new UiKeyInput(Key.Enter, true)));
        Assert.True(f.Send(new UiKeyInput(Key.Enter, false)));
        Assert.Equal(0, f.Clicks);
        f.Pump();
    }

    /// <summary>Checks identical keys from separate devices keep their original window destinations.</summary>
    [Fact]
    public void KeyboardSeriesAreOwnedByDeviceAcrossWindows()
    {
        using var f = new Fixture();
        var secondSource = new TestSource();
        int clicks = 0;
        using var secondSurface = new UiSurface(Button(onClick: () => clicks++));
        UiWindowRegistration registration = f.Windows.Register(secondSource);
        Task<UiWindowBinding> drawing = f.Windows.BindPresentationAsync(registration.Window, secondSurface).AsTask();
        for (int i = 0; i < 10; i++)
        {
            f.Pump(1);
            secondSource.Present();
        }

        UiPopupOwner owner = f.Popups.RegisterOwner(registration.Window, secondSurface);
        f.Surface.MoveFocus(UiFocusNavigation.First);
        secondSurface.MoveFocus(UiFocusNavigation.First);
        f.Popups.Dispatch(new(f.Source.Origin, new UiKeyInput(Key.Enter, true), new InputDeviceId(1)));
        f.Popups.Dispatch(new(secondSource.Origin, new UiKeyInput(Key.Enter, true), new InputDeviceId(2)));
        f.Popups.Dispatch(new(f.Source.Origin, new UiKeyInput(Key.Enter, false), new InputDeviceId(1)));
        f.Popups.Dispatch(new(secondSource.Origin, new UiKeyInput(Key.Enter, false), new InputDeviceId(2)));
        Assert.Equal(1, f.Clicks);
        Assert.Equal(1, clicks);
        owner.Dispose();
        f.Pump();
        Completed(drawing).DisposeAsync();
        f.Pump();
        registration.Dispose();
        f.Pump();
        Assert.True(registration.IsReleased);
    }

    /// <summary>Checks minimize and restore preserve the borrowed root binding while closing its menu.</summary>
    [Fact]
    public void HiddenOwnerKeepsRegistrationAndCanOpenAgain()
    {
        using var f = new Fixture();
        UiPopupHandle popup = f.Open();
        f.Source.Visible = false;
        f.Source.Size = Vector2.Zero;
        f.Pump();
        Assert.Equal(UiPopupState.Closed, popup.State);
        Assert.False(f.Registration.IsReleased);
        Assert.True(f.Windows.IsBound(f.Registration.Window, f.Surface));
        f.Source.Visible = true;
        f.Source.Size = new(200, 150);
        f.Pump();
        Assert.Equal(UiPopupState.Open, f.Open().State);
    }

    /// <summary>Checks declined pointer Down is not retained when the host keeps its terminal on the game side.</summary>
    [Fact]
    public void DeclinedSeriesDoesNotCaptureALaterIndependentDown()
    {
        using var f = new Fixture();
        Assert.False(f.Send(new UiPointerInput(5, UiPointerPhase.Down, new(150, 140))));

        // The host owns that game series and does not send its Up to UI.
        Assert.True(f.Send(new UiPointerInput(5, UiPointerPhase.Down, new(5, 5))));
        Assert.True(f.Send(new UiPointerInput(5, UiPointerPhase.Up, new(5, 5))));
        Assert.Equal(1, f.Clicks);
    }

    /// <summary>Checks a menu waits for editor suspension and retains it through GPU retirement.</summary>
    [Fact]
    public void MenuGuardSpansPreparationAndCompletePresentationDrain()
    {
        using var fixture = new Fixture();
        fixture.Surface.MoveFocus(UiFocusNavigation.First);
        var suspension = new TaskCompletionSource<IAsyncDisposable>();
        int releases = 0;
        using IDisposable registration = fixture.Popups.RegisterMenuGuard(fixture.Owner, _ => new(suspension.Task));
        UiPopupHandle popup = fixture.Open();
        Assert.NotEqual(UiPopupState.Open, popup.State);
        Assert.Same(fixture.Anchor, fixture.Surface.FocusedElement);
        suspension.SetResult(new GuardToken(() => releases++));
        fixture.Pump();
        Assert.Equal(UiPopupState.Open, popup.State);
        fixture.Source.RetireGate = new();
        popup.Close();
        fixture.Pump();
        Assert.Equal(0, releases);
        fixture.Source.RetireGate.SetResult();
        for (int frame = 0; frame < 100 && popup.State != UiPopupState.Closed; frame++)
        {
            fixture.Pump();
            Thread.Yield();
        }

        Assert.Equal(UiPopupState.Closed, popup.State);
        Assert.Equal(1, releases);
    }

    /// <summary>Checks cancellation callbacks observe closing before reentering the popup manager.</summary>
    [Fact]
    public void GuardCancellationObservesClosingAndCanRequestCloseAgain()
    {
        using var fixture = new Fixture();
        UiPopupHandle? popup = null;
        UiPopupState? observed = null;
        CancellationTokenRegistration cancellation = default;
        using IDisposable registration = fixture.Popups.RegisterMenuGuard(fixture.Owner, token =>
        {
            cancellation = token.Register(() =>
            {
                observed = popup!.State;
                popup.Close();
            });
            return ValueTask.FromResult<IAsyncDisposable>(new GuardToken(() => { }));
        });
        popup = fixture.Open();
        popup.Close();
        fixture.Pump();
        Assert.Equal(UiPopupState.Closing, observed);
        Assert.Equal(UiPopupState.Closed, popup.State);
        cancellation.Dispose();
    }

    private sealed class Participant : IAsyncDisposable
    {
        internal TaskCompletionSource Gate { get; } = new();

        internal int Calls { get; private set; }

        public ValueTask DisposeAsync()
        {
            Calls++;
            return new(Gate.Task);
        }
    }

    private sealed class GuardToken(Action release) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            release();
            return ValueTask.CompletedTask;
        }
    }
}
