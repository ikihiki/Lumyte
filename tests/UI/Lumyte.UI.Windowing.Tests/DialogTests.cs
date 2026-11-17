using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Lumyte.UI.Controls;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using static Lumyte.UI.Controls.Windowing.DialogCompose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Uses real Hosted bindings and the same modal policy for physical routing and automation probes.</summary>
public sealed class DialogTests
{
    /// <summary>Modal content receives focus while owner activation, wheel probing and key access are blocked.</summary>
    [Fact]
    public void HostedDialogShieldsOwnerAndRestoresFocusAfterDrain()
    {
        using var fixture = new DialogFixture();
        var open = Signal.Create(false);
        int requests = 0;
        fixture.Set(Panel()[Button(key: "owner", width: 20, height: 20), Dialog("dialog", "Details", isOpen: open, initialClientSize: new(80, 60), onCloseRequested: _ => requests++)[Stack()[Button(key: "first", width: 40, height: 20), Button(key: "last", width: 40, height: 20)]]]);
        UiElement owner = fixture.Base.Surface.RootElement.Children[0];
        Assert.True(fixture.Base.Surface.TryFocus(owner));
        open.Value = true;
        fixture.Pump();
        UiDialogOpened dialog = Assert.Single(fixture.Dialogs.CaptureDialogs());
        Assert.Equal("native-unavailable", dialog.FallbackCode);
        Assert.True(fixture.Dialogs.TryGetSurface(dialog.DialogId, out UiSurface? content));
        Assert.Equal("first", content!.FocusedElement!.Widget.Key);
        Assert.Null(fixture.Base.Popups.ProbePointer(fixture.Base.Surface, new(5)).ActivationTarget);
        Assert.Null(fixture.Base.Popups.ProbeWheel(fixture.Base.Surface, new(5)));
        Assert.False(fixture.Base.Popups.CanReceiveKeys(fixture.Base.Surface));
        Assert.True(fixture.Base.Send(new UiWheelInput(new(5), new(0, -30))));
        fixture.Base.Send(new UiPointerInput(42, UiPointerPhase.Down, new(5)));
        fixture.Base.Send(new UiPointerInput(42, UiPointerPhase.Up, new(5)));
        Assert.Equal(0, fixture.Base.Clicks);
        fixture.Base.Send(new UiKeyInput(Key.Tab, true));
        fixture.Base.Send(new UiKeyInput(Key.Tab, false));
        Assert.Equal("last", content.FocusedElement!.Widget.Key);
        fixture.Base.Send(new UiKeyInput(Key.Tab, true));
        fixture.Base.Send(new UiKeyInput(Key.Tab, false));
        Assert.Equal("first", content.FocusedElement!.Widget.Key);
        fixture.Base.Send(new UiKeyInput(Key.Escape, true));
        fixture.Base.Send(new UiKeyInput(Key.Escape, true, IsRepeat: true));
        fixture.Base.Send(new UiKeyInput(Key.Escape, false));
        Assert.Equal(1, requests);
        Assert.True(open.Value);
        open.Value = false;
        fixture.Pump();
        Assert.Empty(fixture.Dialogs.CaptureDialogs());
        Assert.Same(owner, fixture.Base.Surface.FocusedElement);
        Assert.True(fixture.Base.Popups.CanReceiveKeys(fixture.Base.Surface));
    }

    /// <summary>NativeRequired fails before attempting to instantiate portal content and does not retry every frame.</summary>
    [Fact]
    public void NativeRequiredFailsBeforeContentAndLatchesUntilModelReset()
    {
        using var fixture = new DialogFixture();
        var results = new List<UiDialogClosed>();
        fixture.Set(Panel()[Dialog("native", "Native", isOpen: true, presentation: DialogPresentation.NativeRequired, onClosed: results.Add)[new NoRecipe()]]);
        fixture.Pump();
        Assert.Equal("native-unavailable", Assert.Single(results).Code);
        Assert.Empty(fixture.Dialogs.CaptureDialogs());
        fixture.Pump();
        Assert.Single(results);
        Assert.Single(fixture.Base.Windows.CaptureWindows());
    }

    /// <summary>Rebuilding an open declaration preserves the session and adopts the latest callback.</summary>
    [Fact]
    public void SameKeyRebuildRetainsSessionAndUsesLatestCloseCallback()
    {
        using var fixture = new DialogFixture();
        int oldRequests = 0;
        int latestRequests = 0;
        Widget Build(Action<UiDialogCloseRequest> callback) => Panel()[Dialog("same", "Title", isOpen: true, initialClientSize: new(80, 60), onCloseRequested: callback)[Button(key: "retained", width: 40, height: 20)]];
        fixture.Set(Build(_ => oldRequests++));
        fixture.Pump();
        UiDialogOpened first = Assert.Single(fixture.Dialogs.CaptureDialogs());
        fixture.Dialogs.TryGetSurface(first.DialogId, out UiSurface? content);
        UiElement retained = content!.RootElement.Children[0];
        fixture.Set(Build(_ => latestRequests++));
        fixture.Pump();
        Assert.Equal(first.DialogId, Assert.Single(fixture.Dialogs.CaptureDialogs()).DialogId);
        Assert.Same(retained, content.RootElement.Children[0]);
        fixture.Base.Send(new UiKeyInput(Key.Escape, true));
        fixture.Base.Send(new UiKeyInput(Key.Escape, false));
        Assert.Equal(0, oldRequests);
        Assert.Equal(1, latestRequests);
    }

    /// <summary>An old captured owner press is cancelled before a new modal scope is published.</summary>
    [Fact]
    public void OpeningCancelsOwnerGestureAndConsumesItsTerminalInput()
    {
        using var fixture = new DialogFixture();
        var open = Signal.Create(false);
        int clicks = 0;
        fixture.Set(Panel()[Button(width: 20, height: 20, onClick: () => clicks++), Dialog("capture", "Capture", isOpen: open, initialClientSize: new(80, 60))[Button(width: 40, height: 20)]]);
        fixture.Base.Send(new UiPointerInput(7, UiPointerPhase.Down, new(5)));
        open.Value = true;
        fixture.Pump();
        Assert.True(fixture.Base.Send(new UiPointerInput(7, UiPointerPhase.Up, new(5))));
        Assert.Equal(0, clicks);
    }

    /// <summary>A throwing cancellation callback cannot strand the popup owner or modal token.</summary>
    [Fact]
    public void ThrowingGuardCancellationStillDrainsAllResources()
    {
        using var fixture = new DialogFixture();
        var open = Signal.Create(true);
        int released = 0;
        string? code = null;
        using IDisposable guard = fixture.Base.Popups.RegisterMenuGuard(fixture.Base.Owner, cancellation =>
        {
            CancellationTokenRegistration registration = cancellation.Register(() => throw new InvalidOperationException("cancel"));
            return ValueTask.FromResult<IAsyncDisposable>(new GuardLease(() =>
            {
                released++;
                registration.Dispose();
            }));
        });
        fixture.Set(Panel()[Dialog("guard", "Guard", isOpen: open, initialClientSize: new(80, 60), onClosed: result => code = result.Code)[Button()]]);
        fixture.Pump();
        Assert.Single(fixture.Dialogs.CaptureDialogs());
        open.Value = false;
        fixture.Pump();
        Assert.Empty(fixture.Dialogs.CaptureDialogs());
        Assert.Equal(1, released);
        Assert.Equal("cleanup-failed", code);
        Assert.Single(fixture.Base.Windows.CaptureWindows());
    }

    /// <summary>One invalid replacement cannot prevent another closing session from retiring.</summary>
    [Fact]
    public void InvalidContentReplacementIsLatchedAndDoesNotStarveDrains()
    {
        using var fixture = new DialogFixture();
        int closed = 0;
        fixture.Set(Panel()[Dialog("first", "First", isOpen: true, onClosed: _ => closed++)[Button()], Dialog("second", "Second", isOpen: true, onClosed: _ => closed++)[Button()]]);
        fixture.Pump();
        Assert.Equal(2, fixture.Dialogs.CaptureDialogs().Count);
        fixture.Set(Panel()[Dialog("first", "First", isOpen: true, onClosed: _ => closed++)[new NoRecipe()]]);
        fixture.Pump();
        Assert.Empty(fixture.Dialogs.CaptureDialogs());
        Assert.Equal(2, closed);
        fixture.Pump();
        Assert.Equal(2, closed);
        Assert.Single(fixture.Base.Windows.CaptureWindows());
    }

    /// <summary>A nested modal cannot classify the outer owner's background as an unrelated input root.</summary>
    [Fact]
    public void NestedModalShieldsAllApplicableAncestorScopes()
    {
        using var fixture = new DialogFixture();
        var inner = Signal.Create(false);
        int backgroundClicks = 0;
        Widget nested = Dialog("inner", "Inner", isOpen: inner, initialClientSize: new(40, 40))[Button(width: 20, height: 20)];
        Widget outer = Dialog("outer", "Outer", isOpen: true, initialClientSize: new(120, 100))[Panel()[Button(width: 20, height: 20), nested]];
        fixture.Set(Panel()[Button(width: 20, height: 20, onClick: () => backgroundClicks++), outer]);
        fixture.Pump();
        inner.Value = true;
        fixture.Pump();
        Assert.Equal(2, fixture.Dialogs.CaptureDialogs().Count);
        Assert.True(fixture.Base.Send(new UiPointerInput(99, UiPointerPhase.Down, new(5))));
        Assert.True(fixture.Base.Send(new UiPointerInput(99, UiPointerPhase.Up, new(5))));
        Assert.Equal(0, backgroundClicks);
        Assert.False(fixture.Base.Popups.CanReceiveKeys(fixture.Base.Surface));
    }

    /// <summary>The content close Button routes the current host request and old mounts cannot close a new session.</summary>
    [Fact]
    public void ContentCloseButtonUsesPortalRequestAndRejectsDisposedMount()
    {
        using var fixture = new DialogFixture();
        var open = Signal.Create(true);
        var reasons = new List<UiDialogCloseRequestReason>();
        Widget body = DialogContent()[DialogContent.Close()[Rectangle(width: 12, height: 12)]];
        fixture.Set(Panel()[Dialog("close", "Close", isOpen: open, initialClientSize: new(80, 60), onCloseRequested: request => reasons.Add(request.Reason))[body]]);
        fixture.Pump();
        UiDialogOpened first = Assert.Single(fixture.Dialogs.CaptureDialogs());
        Assert.True(fixture.Dialogs.TryGetSurface(first.DialogId, out UiSurface? content));
        UiAutomationNode button = content!.CaptureAutomationSnapshot().Nodes.Single(node => node.Role == UiRole.Button);
        var old = (Lumyte.UI.Compose.Definitions.Button)content.FindElement(button.Id)!.Widget;
        var point = new System.Numerics.Vector2(button.ActionableBounds.X + 1, button.ActionableBounds.Y + 1);
        fixture.Base.Popups.Dispatch(content, new UiPointerInput(300, UiPointerPhase.Down, point));
        fixture.Base.Popups.Dispatch(content, new UiPointerInput(300, UiPointerPhase.Up, point));
        Assert.Equal(UiDialogCloseRequestReason.CloseButton, Assert.Single(reasons));
        Assert.True(open.Value);
        open.Value = false;
        fixture.Pump();
        open.Value = true;
        fixture.Pump();
        Assert.NotEqual(first.DialogId, Assert.Single(fixture.Dialogs.CaptureDialogs()).DialogId);
        old.OnClick?.Invoke();
        Assert.Single(reasons);
    }

    /// <summary>Modeless outside requests do not block ordinary owner input; Escape uses the same controlled request path.</summary>
    [Fact]
    public void ModelessOutsideAndEscapeRequestsPreserveOrdinaryRouting()
    {
        using var fixture = new DialogFixture();
        var open = Signal.Create(true);
        int clicks = 0;
        var reasons = new List<UiDialogCloseRequestReason>();
        Widget dialog = Dialog("modeless", "Modeless", isOpen: open, isModal: false, closeOnOutsidePress: true, initialClientSize: new(80, 60), onCloseRequested: request => reasons.Add(request.Reason))[Button(width: 40, height: 20)];
        fixture.Set(Panel()[Button(width: 20, height: 20, onClick: () => clicks++), dialog]);
        fixture.Pump();
        fixture.Base.Send(new UiPointerInput(301, UiPointerPhase.Down, new(5)));
        fixture.Base.Send(new UiPointerInput(301, UiPointerPhase.Up, new(5)));
        Assert.Equal(1, clicks);
        Assert.Equal(UiDialogCloseRequestReason.OutsidePress, Assert.Single(reasons));
        UiDialogOpened info = Assert.Single(fixture.Dialogs.CaptureDialogs());
        Assert.True(fixture.Dialogs.TryGetSurface(info.DialogId, out UiSurface? content));
        fixture.Base.Popups.Dispatch(content!, new UiPointerInput(302, UiPointerPhase.Down, new(5)));
        fixture.Base.Popups.Dispatch(content!, new UiPointerInput(302, UiPointerPhase.Up, new(5)));
        fixture.Base.Send(new UiKeyInput(Key.Escape, true));
        fixture.Base.Send(new UiKeyInput(Key.Escape, false));
        Assert.Equal(new[] { UiDialogCloseRequestReason.OutsidePress, UiDialogCloseRequestReason.Escape }, reasons);
        open.Value = false;
        fixture.Pump();
    }

    /// <summary>Close intent rejects old content before the queued owner release, consuming terminal input after drain.</summary>
    [Fact]
    public void ClosingOwnerRejectsInputBeforePopupAdvanceAndOwnsTerminalEvents()
    {
        using var fixture = new DialogFixture();
        var open = Signal.Create(true);
        int clicks = 0;
        fixture.Set(Panel()[Dialog("closing", "Closing", isOpen: open, initialClientSize: new(80, 60))[Button(width: 40, height: 20, onClick: () => clicks++)]]);
        fixture.Pump();
        UiDialogOpened info = Assert.Single(fixture.Dialogs.CaptureDialogs());
        Assert.True(fixture.Dialogs.TryGetSurface(info.DialogId, out UiSurface? content));
        open.Value = false;
        fixture.Base.Surface.UpdateLayout();
        fixture.Dialogs.Advance();
        Assert.Null(fixture.Base.Popups.ProbePointer(content!, new(5)).ActivationTarget);
        Assert.Null(fixture.Base.Popups.ProbeWheel(content!, new(5)));
        Assert.False(fixture.Base.Popups.CanReceiveKeys(content!));
        Assert.True(fixture.Base.Popups.Dispatch(content!, new UiPointerInput(303, UiPointerPhase.Down, new(5))));
        Assert.True(fixture.Base.Send(new UiKeyInput(Key.Space, true)));
        Assert.Equal(0, clicks);
        fixture.Pump();
        Assert.True(fixture.Base.Send(new UiPointerInput(303, UiPointerPhase.Up, new(5))));
        Assert.True(fixture.Base.Send(new UiKeyInput(Key.Space, false)));
        Assert.Equal(0, clicks);
    }

    /// <summary>Unavailable owners defer intent and close each admitted generation only once.</summary>
    /// <param name="initiallyVisible">Whether the owner starts visible.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenOwnerDefersAdmissionAndResumesOneGeneration(bool initiallyVisible)
    {
        using var fixture = new DialogFixture();
        var closed = new List<UiDialogClosed>();
        fixture.Base.Source.Visible = initiallyVisible;
        fixture.Base.Pump();
        fixture.Set(Panel()[Dialog("hidden", "Hidden", isOpen: true, onClosed: closed.Add)[Button()]]);
        fixture.Pump();
        Guid? first = initiallyVisible ? Assert.Single(fixture.Dialogs.CaptureDialogs()).DialogId : null;
        fixture.Base.Source.Visible = false;
        fixture.Pump(80);
        Assert.Empty(fixture.Dialogs.CaptureDialogs());
        Assert.Equal(initiallyVisible ? 1 : 0, closed.Count);
        Assert.Single(fixture.Base.Windows.CaptureWindows());
        fixture.Base.Source.Visible = true;
        fixture.Pump();
        UiDialogOpened resumed = Assert.Single(fixture.Dialogs.CaptureDialogs());
        Assert.NotEqual(first, resumed.DialogId);
        fixture.Pump(80);
        Assert.Equal(resumed.DialogId, Assert.Single(fixture.Dialogs.CaptureDialogs()).DialogId);
        Assert.Equal(initiallyVisible ? 1 : 0, closed.Count);
    }

    /// <summary>Hide and reshow cannot overlap an old drain; the latest declaration supplies the replacement.</summary>
    [Fact]
    public void HiddenOwnerWaitsForOldDrainAndUsesLatestGeneration()
    {
        using var fixture = new DialogFixture();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int oldOpened = 0;
        int newOpened = 0;
        int closed = 0;
        fixture.Set(Panel()[Dialog("generation", "Old", isOpen: true, onOpened: _ => oldOpened++, onClosed: _ => closed++)[Button(key: "old")]]);
        fixture.Pump();
        UiDialogOpened original = Assert.Single(fixture.Dialogs.CaptureDialogs());
        fixture.Dialogs.BindLifetime(original.DialogId, new DelayedLifetime(gate.Task));
        fixture.Base.Source.Visible = false;
        fixture.Pump();
        fixture.Base.Source.Visible = true;
        fixture.Set(Panel()[Dialog("generation", "New", isOpen: true, onOpened: _ => newOpened++, onClosed: _ => closed++)[Button(key: "new")]]);
        fixture.Pump(80);
        Assert.Empty(fixture.Dialogs.CaptureDialogs());
        Assert.Equal(1, oldOpened);
        Assert.Equal(0, newOpened);
        Assert.Equal(0, closed);
        Assert.Equal(2, fixture.Base.Windows.CaptureWindows().Count);
        gate.SetResult();
        fixture.Pump(80);
        UiDialogOpened replacement = Assert.Single(fixture.Dialogs.CaptureDialogs());
        Assert.NotEqual(original.DialogId, replacement.DialogId);
        Assert.Equal(1, newOpened);
        Assert.Equal(1, closed);
        Assert.Equal(2, fixture.Base.Windows.CaptureWindows().Count);
        Assert.True(fixture.Dialogs.TryGetSurface(replacement.DialogId, out UiSurface? content));
        Assert.Equal("new", content!.RootElement.Children[0].Widget.Key);
    }

    private sealed class DelayedLifetime(Task gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(gate);
    }

    private sealed class GuardLease(Action release) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NoRecipe : Widget
    {
        protected override UiElement CreateElement() => throw new InvalidOperationException("Native capability checks must run first.");
    }

    private sealed class DialogFixture : IDisposable
    {
        internal DialogFixture()
        {
            Dialogs = new(Base.Windows, Base.Popups, Base.Owner, Base.Registration.Window, Base.Surface);
        }

        internal Fixture Base { get; } = new();

        internal UiDialogPortalCoordinator Dialogs { get; }

        public void Dispose()
        {
            Dialogs.Dispose();
            Pump(40);
            Assert.True(Dialogs.IsStopped);
            Base.Dispose();
        }

        internal void Set(Widget root)
        {
            Base.Surface.Rebuild(root);
            Base.Surface.UpdateLayout();
        }

        internal void Pump(int count = 24)
        {
            for (int index = 0; index < count; index++)
            {
                Base.Surface.UpdateLayout();
                Base.Pump(1);
                Dialogs.Advance();
            }
        }
    }
}
