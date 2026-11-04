using Lumyte.Input.Abstractions;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Checks generic focus and key routing, traversal, ownership and component activation.</summary>
public sealed class KeyboardTests : UiElementTestBase
{
    /// <summary>Checks Tab and Shift-Tab cycle prepared candidates, including zero-area and clipped candidates.</summary>
    [Fact]
    public void TabCyclesEligibleCandidatesAndConsumesItsTerminalUp()
    {
        Definitions.Button first = MakeButton();
        Definitions.Button zero = Button();
        Definitions.Button last = MakeButton();
        using UiSurface surface = Surface(Stack()[first, Button(focusable: false), zero, Panel(height: 200), last]);
        Assert.Null(surface.FocusedElement?.Widget);
        Tab(surface);
        Assert.Same(first, surface.FocusedElement?.Widget);
        Tab(surface);
        Assert.Same(zero, surface.FocusedElement?.Widget);
        Tab(surface);
        Assert.Same(last, surface.FocusedElement?.Widget);
        Tab(surface);
        Assert.Same(first, surface.FocusedElement?.Widget);
        Tab(surface, true);
        Assert.Same(last, surface.FocusedElement?.Widget);
    }

    /// <summary>Checks no-candidate behavior and the one-candidate traversal contract.</summary>
    [Fact]
    public void EmptyAndSingleCandidateTraversal()
    {
        using UiSurface empty = Surface(Panel());
        Assert.False(KeyDown(empty, Key.Tab));
        Assert.False(KeyUp(empty, Key.Tab));
        using UiSurface single = Surface(MakeButton());
        Tab(single, true);
        Widget? candidate = single.FocusedElement?.Widget;
        Tab(single);
        Assert.Same(candidate, single.FocusedElement?.Widget);
    }

    /// <summary>Checks input selection and exclusion flags determine focus candidates, not owned background slots.</summary>
    [Fact]
    public void FocusCandidatesExcludeDisabledSubtreesAndButtonBackground()
    {
        Definitions.Button background = MakeButton();
        Definitions.Button expected = Button()[Panel(width: 20, height: 10), Button.Background()[background]];
        using UiSurface surface = Surface(Stack()[Panel(isEnabled: false)[MakeButton()], Panel(isVisible: false)[MakeButton()], Panel(hitTestVisible: false)[MakeButton()], expected]);
        Tab(surface);
        Assert.Same(expected, surface.FocusedElement?.Widget);
        Tab(surface);
        Assert.Same(expected, surface.FocusedElement?.Widget);
    }

    /// <summary>Checks modifier shortcuts do not accidentally trigger focus traversal.</summary>
    /// <param name="modifier">The non-navigation modifier.</param>
    [Theory]
    [InlineData(UiModifiers.Control)]
    [InlineData(UiModifiers.Alt)]
    [InlineData(UiModifiers.Meta)]
    public void ModifiedTabIsNotReserved(UiModifiers modifier)
    {
        using UiSurface surface = Surface(MakeButton());
        Assert.False(KeyDown(surface, Key.Tab, modifiers: modifier));
        Assert.Null(surface.FocusedElement?.Widget);
    }

    /// <summary>Checks Button activation happens once on Up, not on Down, duplicate Down or repeat.</summary>
    /// <param name="key">The activation key.</param>
    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void ButtonRecognizesCapturedActivationOnUp(Key key)
    {
        int calls = 0;
        Definitions.Button button = MakeButton(() => calls++);
        using UiSurface surface = Surface(Panel()[button]);
        Tab(surface);
        Assert.True(KeyDown(surface, key));
        Assert.True(Pressed(button));
        Assert.True(KeyDown(surface, key));
        Assert.True(KeyDown(surface, key, true));
        Assert.Equal(0, calls);
        button.OnClick = () => calls += 2;
        Assert.True(KeyUp(surface, key));
        Assert.False(Pressed(button));
        Assert.Equal(2, calls);
        Assert.False(KeyUp(surface, key));
        button.OnClick = null;
        Assert.True(KeyDown(surface, key));
        Assert.True(KeyUp(surface, key));
        Assert.Equal(2, calls);
    }

    /// <summary>Checks orphan repeat and modified activation do not begin a press.</summary>
    [Fact]
    public void OrphanRepeatAndModifiedActivationDoNotStartPress()
    {
        Definitions.Button button = MakeButton();
        using UiSurface surface = Surface(button);
        Tab(surface);
        Assert.False(KeyDown(surface, Key.Enter, true));
        Assert.False(KeyDown(surface, Key.Space, modifiers: UiModifiers.Control));
        Assert.False(Pressed(button));
    }

    /// <summary>Checks focus movement cancels the key owner while retaining consumption through its physical Up.</summary>
    [Fact]
    public void FocusMovementCancelsActivationWithoutLeakingUp()
    {
        int calls = 0;
        Definitions.Button first = MakeButton(() => calls++);
        Definitions.Button second = MakeButton();
        using UiSurface surface = Surface(Stack()[first, second]);
        Tab(surface);
        KeyDown(surface, Key.Enter);
        Tab(surface);
        Assert.Same(second, surface.FocusedElement?.Widget);
        Assert.False(Pressed(first));
        Assert.True(KeyDown(surface, Key.Enter, true));
        Assert.True(KeyUp(surface, Key.Enter));
        Assert.False(KeyUp(surface, Key.Enter));
        Assert.Equal(0, calls);
    }

    /// <summary>Checks pointer and key activation share a component owner, while generic capture remains independent.</summary>
    [Fact]
    public void PointerAndKeyOwnersCannotDoubleActivateButton()
    {
        int calls = 0;
        Definitions.Button first = MakeButton(() => calls++);
        Definitions.Button second = MakeButton();
        using UiSurface surface = Surface(Stack()[first, second]);
        Assert.True(Pointer(surface, UiPointerPhase.Down, 1, 1));
        Assert.Same(first, surface.FocusedElement?.Widget);
        Assert.True(KeyDown(surface, Key.Enter));
        Assert.True(KeyUp(surface, Key.Enter));
        Assert.True(Pressed(first));
        Tab(surface);
        Assert.Same(second, surface.FocusedElement?.Widget);
        Assert.True(Pressed(first));
        Assert.True(Pointer(surface, UiPointerPhase.Up, 1, 1));
        Assert.Equal(1, calls);
        Tab(surface);
        KeyDown(surface, Key.Space);
        Assert.True(Pointer(surface, UiPointerPhase.Down, 1, 1));
        Assert.True(Pressed(first));
        Assert.False(Pointer(surface, UiPointerPhase.Up, 1, 1));
        Assert.True(KeyUp(surface, Key.Space));
        Assert.Equal(2, calls);
    }

    /// <summary>Checks a competing activation key is consumed without stealing an existing key owner.</summary>
    [Fact]
    public void CompetingActivationKeyIsConsumedThroughUp()
    {
        int calls = 0;
        Definitions.Button button = MakeButton(() => calls++);
        using UiSurface surface = Surface(button);
        Tab(surface);
        KeyDown(surface, Key.Enter);
        Assert.True(KeyDown(surface, Key.Space));
        Assert.True(KeyUp(surface, Key.Space));
        Assert.True(Pressed(button));
        KeyUp(surface, Key.Enter);
        Assert.Equal(1, calls);
    }

    /// <summary>Checks clicking the same focused target preserves focus and clicking background clears it.</summary>
    [Fact]
    public void PointerFocusAndBackgroundPolicyIsGeneric()
    {
        Definitions.Button first = MakeButton();
        Definitions.Button noFocus = Button(focusable: false)[Panel(width: 20, height: 10)];
        using UiSurface surface = Surface(Stack()[first, noFocus]);
        Pointer(surface, UiPointerPhase.Down, 1, 1);
        Pointer(surface, UiPointerPhase.Up, 1, 1);
        Assert.Same(first, surface.FocusedElement?.Widget);
        Pointer(surface, UiPointerPhase.Down, 1, 1);
        Pointer(surface, UiPointerPhase.Up, 1, 1);
        Assert.Same(first, surface.FocusedElement?.Widget);
        Pointer(surface, UiPointerPhase.Down, 1, 11);
        Assert.Null(surface.FocusedElement?.Widget);
        Pointer(surface, UiPointerPhase.Up, 1, 11);
        Tab(surface);
        Assert.False(Pointer(surface, UiPointerPhase.Down, 90, 90));
        Assert.Null(surface.FocusedElement?.Widget);
    }

    /// <summary>Checks a custom editor receives focused keys, repeats, terminal Up and text inputs without activating an outer Button.</summary>
    [Fact]
    public void CustomEditorInsideButtonHandlesKeysAndText()
    {
        int clicks = 0;
        int downs = 0;
        int ups = 0;
        string text = string.Empty;
        var editor = new Control { Focusable = true, Width = 20, Height = 10 };
        editor.Handle = context =>
        {
            switch (context.Input)
            {
                case UiPointerInput { Phase: UiPointerPhase.Down }:
                    Assert.True(context.RequestFocus());
                    Assert.True(context.CapturePointer());
                    return UiInputResult.Handled;
                case UiKeyInput { IsDown: true }:
                    Assert.True(context.IsFocused);
                    Assert.True(context.CaptureKey());
                    downs++;
                    return UiInputResult.Handled;
                case UiKeyInput:
                    Assert.True(context.IsKeyCaptured);
                    Assert.False(context.ReleaseKeyCapture());
                    ups++;
                    return UiInputResult.Handled;
                case TextCommit commit:
                    text += commit.Text;
                    return UiInputResult.Handled;
                default:
                    return UiInputResult.Unhandled;
            }
        };
        using UiSurface surface = Surface(Button(onClick: () => clicks++)[editor]);
        Pointer(surface, UiPointerPhase.Down, 1, 1);
        Pointer(surface, UiPointerPhase.Up, 1, 1);
        Assert.Same(editor, surface.FocusedElement?.Widget);
        Assert.True(KeyDown(surface, Key.Enter));
        Assert.True(KeyDown(surface, Key.Enter, true));
        Assert.True(KeyUp(surface, Key.Enter));
        Assert.True(surface.Dispatch(new TextCommit("日本語")));
        Assert.Equal(2, downs);
        Assert.Equal(1, ups);
        Assert.Equal("日本語", text);
        Assert.Equal(0, clicks);
    }

    /// <summary>Checks a receiver can consume or block Tab before default traversal runs.</summary>
    /// <param name="result">The component's propagation decision.</param>
    [Theory]
    [InlineData(UiInputResult.Handled)]
    [InlineData(UiInputResult.StopPropagation)]
    public void ComponentCanOverrideTabTraversal(UiInputResult result)
    {
        var first = new Control
        {
            Focusable = true,
            Handle = context => context.Input is UiKeyInput { Key: Key.Tab, IsDown: true } ? result : UiInputResult.Unhandled,
        };
        using UiSurface surface = Surface(Stack()[first, MakeButton()]);
        Tab(surface);
        Assert.Equal(result == UiInputResult.Handled, KeyDown(surface, Key.Tab));
        Assert.Same(first, surface.FocusedElement?.Widget);
        Assert.Equal(result == UiInputResult.Handled, KeyUp(surface, Key.Tab));
    }

    /// <summary>Checks runtime eligibility changes are applied by input preparation and cancel an existing key owner.</summary>
    [Fact]
    public void InputRefreshWithdrawsFocusAndCancelsItsKeyOwner()
    {
        var root = new SelectionPanel();
        Definitions.Button button = MakeButton();
        root.Children.Initialize(button);
        using UiSurface surface = Surface(root);
        Tab(surface);
        KeyDown(surface, Key.Enter);
        root.Select();
        surface.UpdateLayout();
        Assert.Null(surface.FocusedElement?.Widget);
        Assert.False(Pressed(button));
        Assert.True(KeyUp(surface, Key.Enter));
        Assert.False(KeyDown(surface, Key.Tab));
    }

    /// <summary>Checks explicit key cancellation is idempotent and its terminal Up remains consumed.</summary>
    [Fact]
    public void KeyCancellationPreservesConsumptionUntilPhysicalUp()
    {
        Definitions.Button button = MakeButton();
        using UiSurface surface = Surface(button);
        Tab(surface);
        KeyDown(surface, Key.Enter);
        Assert.True(surface.Dispatch(new UiKeyCancelInput(Key.Enter)));
        Assert.True(surface.Dispatch(new UiKeyCancelInput(Key.Enter)));
        Assert.False(Pressed(button));
        Assert.True(KeyUp(surface, Key.Enter));
        Assert.False(surface.Dispatch(new UiKeyCancelInput(Key.Enter)));
    }

    /// <summary>Checks key handler failure cancels recognition without leaking the corresponding Up to the host.</summary>
    [Fact]
    public void FailedKeyHandlerCancelsItsCapture()
    {
        int cancelled = 0;
        var control = new Control { Focusable = true };
        control.Handle = context =>
        {
            if (context.Input is UiKeyInput { Key: Key.A, IsDown: true })
            {
                context.CaptureKey();
                throw new ArithmeticException("fixture");
            }

            if (context.Input is UiKeyCancelInput)
            {
                cancelled++;
            }

            return UiInputResult.Unhandled;
        };
        using UiSurface surface = Surface(control);
        Tab(surface);
        Assert.Throws<ArithmeticException>(() => KeyDown(surface, Key.A));
        Assert.Equal(1, cancelled);
        Assert.True(KeyUp(surface, Key.A));
    }

    /// <summary>Checks a failed activation does not cancel a new series started after terminal release.</summary>
    [Fact]
    public void FailedClickPreservesReentrantNewKeySeries()
    {
        Definitions.Button button = MakeButton();
        using UiSurface surface = Surface(button);
        Tab(surface);
        button.OnClick = () =>
        {
            button.OnClick = null;
            Assert.True(KeyDown(surface, Key.Enter));
            throw new ArithmeticException("fixture");
        };
        KeyDown(surface, Key.Enter);
        Assert.Throws<ArithmeticException>(() => KeyUp(surface, Key.Enter));
        Assert.True(Pressed(button));
        surface.Dispatch(new UiKeyCancelInput(Key.Enter));
        Assert.False(Pressed(button));
        Assert.True(KeyUp(surface, Key.Enter));
    }

    /// <summary>Checks reentrant focus redirection does not send a stale gained notification.</summary>
    [Fact]
    public void ReentrantFocusChangeDoesNotNotifyStaleTarget()
    {
        var first = new Control { Focusable = true };
        var second = new Control { Focusable = true };
        int secondGained = 0;
        bool redirect = false;
        first.Handle = context =>
        {
            if (redirect && context.Input is UiFocusChangedInput { IsFocused: false })
            {
                Assert.True(context.RequestFocus());
            }

            return UiInputResult.Unhandled;
        };
        second.Handle = context =>
        {
            if (context.Input is UiFocusChangedInput { IsFocused: true })
            {
                secondGained++;
            }

            return UiInputResult.Unhandled;
        };
        using UiSurface surface = Surface(Stack()[first, second]);
        Tab(surface);
        redirect = true;
        Tab(surface);
        Assert.Same(first, surface.FocusedElement?.Widget);
        Assert.Equal(0, secondGained);
        redirect = false;
    }

    /// <summary>Checks cancellation can refocus its owner without a later stale loss notification.</summary>
    [Fact]
    public void KeyCancellationRefocusDoesNotReceiveStaleLoss()
    {
        var first = new Control { Focusable = true };
        var second = new Control { Focusable = true };
        var notifications = new List<bool>();
        first.Handle = context =>
        {
            if (context.Input is UiFocusChangedInput focus)
            {
                notifications.Add(focus.IsFocused);
            }
            else if (context.Input is UiKeyInput { IsDown: true })
            {
                Assert.True(context.CaptureKey());
                return UiInputResult.Handled;
            }
            else if (context.Input is UiKeyCancelInput)
            {
                Assert.True(context.RequestFocus());
            }

            return UiInputResult.Unhandled;
        };
        using UiSurface surface = Surface(Stack()[first, second]);
        Assert.True(surface.TryFocus(Instance(first)));
        Assert.True(KeyDown(surface, Key.A));
        Assert.True(surface.TryFocus(Instance(second)));
        Assert.Same(first, surface.FocusedElement?.Widget);
        Assert.True(notifications[^1]);
    }

    /// <summary>Checks a reset inside an unhandled Tab prevents stale default traversal and consumption.</summary>
    [Fact]
    public void UnhandledTabCannotRestoreFocusAfterReentrantFocusLoss()
    {
        UiSurface? surface = null;
        var control = new Control { Focusable = true };
        control.Handle = context =>
        {
            if (context.Input is UiKeyInput { Key: Key.Tab, IsDown: true })
            {
                surface!.Dispatch(new UiFocusLostInput());
            }

            return UiInputResult.Unhandled;
        };
        using UiSurface connected = Surface(Stack()[control, MakeButton()]);
        surface = connected;
        Assert.True(connected.TryFocus(Instance(control)));
        Assert.False(KeyDown(connected, Key.Tab));
        Assert.Null(connected.FocusedElement?.Widget);
        Assert.False(KeyUp(connected, Key.Tab));
    }

    /// <summary>Checks focus notification exceptions leave the selected focus committed and still notify its gained state.</summary>
    [Fact]
    public void FocusLossFailureDoesNotSkipNewFocusNotification()
    {
        var first = new Control { Focusable = true };
        var second = new Control { Focusable = true };
        bool gained = false;
        first.Handle = context => context.Input is UiFocusChangedInput { IsFocused: false }
            ? throw new ArithmeticException("fixture") : UiInputResult.Unhandled;
        second.Handle = context =>
        {
            if (context.Input is UiFocusChangedInput { IsFocused: true })
            {
                gained = true;
            }

            return UiInputResult.Unhandled;
        };
        using UiSurface surface = Surface(Stack()[first, second]);
        Tab(surface);
        Assert.Throws<ArithmeticException>(() => KeyDown(surface, Key.Tab));
        Assert.Same(second, surface.FocusedElement?.Widget);
        Assert.True(gained);
        Assert.True(KeyUp(surface, Key.Tab));
    }

    /// <summary>Checks input values without enum-definition scans and retains unknown non-sentinel keys for custom handlers.</summary>
    [Fact]
    public void InputValidationDoesNotScanKeyDefinitions()
    {
        var control = new Control { Handle = context => context.Input is UiKeyInput ? UiInputResult.Handled : UiInputResult.Unhandled };
        using UiSurface surface = Surface(control);
        Assert.False(KeyDown(surface, Key.Unknown));
        Assert.True(KeyDown(surface, (Key)9999));
        Assert.True(KeyUp(surface, (Key)9999));
        Assert.Throws<ArgumentException>(() => surface.Dispatch(new UiKeyInput(Key.A, false, true)));
        Assert.Throws<ArgumentOutOfRangeException>(() => KeyDown(surface, Key.A, modifiers: (UiModifiers)128));
    }

    /// <summary>Checks focus, pointer and key consumption are cleaned together even if a component's cancellation fails.</summary>
    [Fact]
    public void FocusLossCleansEveryInputDomainDespiteFailure()
    {
        int keyCancels = 0;
        int pointerCancels = 0;
        bool lostFocus = false;
        var control = new Control { Focusable = true };
        control.Handle = context =>
        {
            switch (context.Input)
            {
                case UiPointerInput { Phase: UiPointerPhase.Down }:
                    context.RequestFocus();
                    context.CapturePointer();
                    return UiInputResult.Handled;
                case UiPointerInput { Phase: UiPointerPhase.Cancel }:
                    pointerCancels++;
                    return UiInputResult.Handled;
                case UiKeyInput { IsDown: true }:
                    context.CaptureKey();
                    return UiInputResult.Handled;
                case UiKeyCancelInput cancel:
                    keyCancels++;
                    if (cancel.Key == Key.A)
                    {
                        throw new ArithmeticException("fixture");
                    }

                    return UiInputResult.Handled;
                case UiFocusChangedInput { IsFocused: false }:
                    lostFocus = true;
                    return UiInputResult.Unhandled;
                default:
                    return UiInputResult.Unhandled;
            }
        };
        using UiSurface surface = Surface(control);
        Pointer(surface, UiPointerPhase.Down, 1, 1);
        KeyDown(surface, Key.A);
        KeyDown(surface, Key.B);
        Assert.Throws<ArithmeticException>(() => surface.Dispatch(new UiFocusLostInput()));
        Assert.Null(surface.FocusedElement?.Widget);
        Assert.True(lostFocus);
        Assert.Equal(2, keyCancels);
        Assert.Equal(1, pointerCancels);
        Assert.False(KeyUp(surface, Key.A));
        Assert.False(KeyUp(surface, Key.B));
        Assert.False(Pointer(surface, UiPointerPhase.Cancel, 1, 1));
    }

    /// <summary>Checks focus and keys remain surface-local and permit sequential worker delivery.</summary>
    /// <returns>The sequential cross-thread verification.</returns>
    [Fact]
    public async Task FocusAndKeysAreSurfaceLocalWithoutThreadAffinityAsync()
    {
        Definitions.Button first = MakeButton();
        Definitions.Button second = MakeButton();
        using UiSurface a = Surface(first);
        using UiSurface b = Surface(second);
        await Task.Run(() => Tab(a));
        Assert.Same(first, a.FocusedElement?.Widget);
        Assert.Null(b.FocusedElement?.Widget);
        Tab(b);
        Assert.True(await Task.Run(() => KeyDown(a, Key.Enter)));
        Assert.False(Pressed(second));
        Assert.True(KeyUp(a, Key.Enter));
    }

    /// <summary>Checks a reset inside a consumed Down does not resurrect that series after the handler returns.</summary>
    [Fact]
    public void ReentrantFocusLossDoesNotRestoreKeyConsumption()
    {
        UiSurface? surface = null;
        var control = new Control();
        control.Handle = context =>
        {
            if (context.Input is UiKeyInput { Key: Key.A, IsDown: true })
            {
                context.CaptureKey();
                Assert.True(surface!.Dispatch(new UiFocusLostInput()));
                return UiInputResult.Handled;
            }

            return UiInputResult.Unhandled;
        };
        using UiSurface connected = Surface(control);
        surface = connected;
        Assert.True(KeyDown(connected, Key.A));
        Assert.False(KeyUp(connected, Key.A));
    }

    /// <summary>Checks repeated delivery through a stable key owner performs no managed allocation.</summary>
    [Fact]
    public void CapturedKeyRepeatAllocatesNoManagedObjects()
    {
        using UiSurface surface = Surface(MakeButton());
        Tab(surface);
        KeyDown(surface, Key.Enter);
        var repeat = new UiKeyInput(Key.Enter, true, true);
        for (int index = 0; index < 256; index++)
        {
            surface.Dispatch(repeat);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            surface.Dispatch(repeat);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(KeyUp(surface, Key.Enter));
    }

    /// <summary>Checks explicit key release sends cancellation and retains only physical-series consumption.</summary>
    [Fact]
    public void ComponentCanReleaseKeyCaptureDuringRepeat()
    {
        int cancelled = 0;
        var control = new Control();
        control.Handle = context =>
        {
            if (context.Input is UiKeyInput { Key: Key.A, IsDown: true } key)
            {
                if (key.IsRepeat)
                {
                    Assert.True(context.ReleaseKeyCapture());
                }
                else
                {
                    Assert.True(context.CaptureKey());
                }

                return UiInputResult.Handled;
            }

            if (context.Input is UiKeyCancelInput)
            {
                cancelled++;
            }

            return UiInputResult.Unhandled;
        };
        using UiSurface surface = Surface(control);
        Assert.True(KeyDown(surface, Key.A));
        Assert.True(KeyDown(surface, Key.A, true));
        Assert.Equal(1, cancelled);
        Assert.True(KeyUp(surface, Key.A));
        Assert.False(KeyUp(surface, Key.A));
    }

    /// <summary>Checks value equality on widgets cannot replace reference identity in traversal.</summary>
    [Fact]
    public void FocusTraversalUsesReferenceIdentity()
    {
        var first = new EqualControl { Focusable = true };
        var second = new EqualControl { Focusable = true };
        using UiSurface surface = Surface(Stack()[first, second]);
        Tab(surface);
        Assert.Same(first, surface.FocusedElement?.Widget);
        Tab(surface);
        Assert.Same(second, surface.FocusedElement?.Widget);
        Tab(surface);
        Assert.Same(first, surface.FocusedElement?.Widget);
    }

    /// <summary>Checks Dispatch does not prepare focus candidates or execute layout implicitly.</summary>
    [Fact]
    public void KeyboardDispatchDoesNotUpdateLayout()
    {
        Definitions.Button button = MakeButton();
        using UiSurface surface = Mount(button);
        surface.SetViewport(new(100, 100));
        Assert.False(KeyDown(surface, Key.Tab));
        Assert.Null(surface.FocusedElement?.Widget);
        Assert.Equal(default, Instance(button).Size);
        surface.UpdateLayout();
        Tab(surface);
        Assert.Same(button, surface.FocusedElement?.Widget);
    }

    /// <summary>Checks a component may close the surface from capture acquisition without a subsequent focus request reviving it.</summary>
    [Fact]
    public void ClosingOnPointerCaptureDoesNotRequestFocusOnDisposedSurface()
    {
        var button = new ClosingButton();
        button.Children.Initialize(Panel(width: 20, height: 10));
        using UiSurface surface = Surface(button);
        button.Acquired = surface.Dispose;
        Assert.True(Pointer(surface, UiPointerPhase.Down, 1, 1));
        Assert.False(Pressed(button));
        Assert.Throws<ObjectDisposedException>(() => surface.Dispatch(new UiFocusLostInput()));
    }

    private static Definitions.Button MakeButton(Action? callback = null) => Button(onClick: callback)[Panel(width: 20, height: 10)];

    private static bool KeyDown(UiSurface surface, Key key, bool repeat = false, UiModifiers modifiers = UiModifiers.None) =>
        surface.Dispatch(new UiKeyInput(key, true, repeat, modifiers));

    private static bool KeyUp(UiSurface surface, Key key) => surface.Dispatch(new UiKeyInput(key, false));

    private static bool Pointer(UiSurface surface, UiPointerPhase phase, float x, float y) =>
        surface.Dispatch(new UiPointerInput(0, phase, new(x, y)));

    private static void Tab(UiSurface surface, bool reverse = false)
    {
        Assert.True(KeyDown(surface, Key.Tab, modifiers: reverse ? UiModifiers.Shift : UiModifiers.None));
        Assert.True(KeyUp(surface, Key.Tab));
    }

    private UiSurface Surface(Widget root)
    {
        UiSurface surface = Mount(root);
        surface.SetViewport(new(100, 100));
        surface.UpdateLayout();
        return surface;
    }

    private sealed record TextCommit(string Text) : UiInput;

    private sealed class Control : Definitions.Panel
    {
        public Func<UiInputContext, UiInputResult>? Handle { get; set; }

        protected override UiElement CreateElement() => new ControlElement(this);
    }

    private sealed class ControlElement(Control widget) : PanelElement(widget)
    {
        protected override UiInputResult OnInput(in UiInputContext context) => ((Control)Widget).Handle?.Invoke(context) ?? UiInputResult.Unhandled;
    }

    private sealed class ClosingButton : Definitions.Button
    {
        public Action? Acquired { get; set; }

        protected override UiElement CreateElement() => new ClosingElement(this);
    }

    private sealed class ClosingElement(ClosingButton widget) : ButtonElement(widget)
    {
        protected override void OnPointerCaptureChanged(UiPointerCaptureState state)
        {
            base.OnPointerCaptureChanged(state);
            if (state.IsCaptured)
            {
                ((ClosingButton)Widget).Acquired?.Invoke();
            }
        }
    }

    private sealed class EqualControl : Definitions.Panel
    {
        public override bool Equals(object? obj) => obj is EqualControl;

        public override int GetHashCode() => 0;
    }

    private sealed class SelectionPanel : Definitions.Panel
    {
        public Widget[]? Selection { get; private set; }

        public SelectionElement? Runtime { get; private set; }

        public void Select(params Widget[] children)
        {
            Selection = children;
            Runtime?.RefreshInput();
        }

        protected override UiElement CreateElement() => Runtime = new(this);
    }

    private sealed class SelectionElement(SelectionPanel widget) : PanelElement(widget)
    {
        public void RefreshInput() => InvalidateInput();

        protected override IEnumerable<UiElement> GetInputChildren() => ((SelectionPanel)Widget).Selection is { } selected
            ? selected.Select(child => Children.FirstOrDefault(element => ReferenceEquals(element.Widget, child)) ?? child.CreateInstance())
            : base.GetInputChildren();
    }
}
