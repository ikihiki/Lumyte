using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;

namespace Lumyte.UI.Controls.Tests;

/// <summary>Exercises generated public controls through ordinary dispatch and detached semantics.</summary>
public sealed class SelectionTests
{
    /// <summary>Shares declarations without sharing accepted values or mutating configuration.</summary>
    [Fact]
    public void SharedDeclarationKeepsIndependentValuesAndExplicitAdoptionResets()
    {
        Compose.Definitions.ToggleButton declaration = ToggleButton();
        using UiSurface first = Surface(declaration);
        using UiSurface second = Surface(declaration);
        Click(first);
        Assert.True(Assert.IsType<ToggleButtonElement>(first.RootElement).IsChecked);
        Assert.False(Assert.IsType<ToggleButtonElement>(second.RootElement).IsChecked);
        Assert.False(declaration.IsChecked);
        first.UpdateLayout();
        Assert.True(first.CaptureAutomationSnapshot().Nodes[0].IsChecked);
        first.Rebuild(ToggleButton());
        first.UpdateLayout();
        Assert.False(first.CaptureAutomationSnapshot().Nodes[0].IsChecked);
    }

    /// <summary>Commits mixed values before the latest callback, and never notifies declarations.</summary>
    [Fact]
    public void ThreeStateCycleAndDesiredStateCommitExactlyOnce()
    {
        var changes = new List<bool?>();
        Compose.Definitions.CheckBox declaration = CheckBox(threeState: true, onChanged: changes.Add);
        using UiSurface surface = Surface(declaration);
        Click(surface);
        Click(surface);
        Assert.Null(Assert.IsType<CheckBoxElement>(surface.RootElement).IsChecked);
        Assert.Equal(new bool?[] { true, null }, changes);
        declaration.OnChanged = value =>
        {
            Assert.Equal(value, Assert.IsType<CheckBoxElement>(surface.RootElement).IsChecked);
            changes.Add(value);
        };
        Assert.True(surface.Dispatch(new UiCheckInput(surface.RootElement.AutomationId, false, new(2))));
        Assert.False(surface.CaptureAutomationSnapshot().Nodes[0].IsChecked);
        Assert.True(surface.Dispatch(new UiCheckInput(surface.RootElement.AutomationId, false, new(2))));
        Assert.Equal(3, changes.Count);
        Assert.False(declaration.IsChecked);
    }

    /// <summary>Keyboard repeat, cancel and focus loss share the original Button recognizer.</summary>
    [Fact]
    public void KeyboardRepeatAndCancellationDoNotDuplicateCallback()
    {
        int calls = 0;
        using UiSurface surface = Surface(Switch(onChanged: _ => calls++));
        Assert.True(surface.TryFocus(surface.RootElement));
        surface.Dispatch(new UiKeyInput(Key.Space, true));
        surface.Dispatch(new UiKeyInput(Key.Space, true, true));
        surface.Dispatch(new UiKeyInput(Key.Space, false));
        Assert.Equal(1, calls);
        surface.Dispatch(new UiKeyInput(Key.Enter, true));
        surface.Dispatch(new UiFocusLostInput());
        surface.Dispatch(new UiKeyInput(Key.Enter, false));
        Assert.Equal(1, calls);
        Assert.False(Assert.IsType<SwitchElement>(surface.RootElement).IsPressed);
    }

    /// <summary>Callbacks observe committed values and failures do not retain capture.</summary>
    [Fact]
    public void CallbackExceptionRetainsCommittedValueAndReentrantPressIsIndependent()
    {
        int calls = 0;
        UiSurface? current = null;
        using UiSurface surface = Surface(ToggleButton(onChanged: _ =>
        {
            calls++;
            Assert.True(Assert.IsType<ToggleButtonElement>(current!.RootElement).IsChecked);
            throw new InvalidOperationException("callback");
        }));
        current = surface;
        Assert.Throws<InvalidOperationException>(() => Click(surface));
        Assert.Equal(1, calls);
        Assert.True(surface.CaptureAutomationSnapshot().Nodes[0].IsChecked);
        Assert.False(surface.CaptureAutomationSnapshot().Nodes[0].HasActivePress);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(2)));
        Assert.Equal(1, calls);
    }

    /// <summary>Radio derives all bools from one group key, navigates and preserves keyed identities.</summary>
    [Fact]
    public void RadioSelectNavigateReorderRemoveAndInvalidPrepare()
    {
        var changes = new List<string?>();
        using UiSurface surface = Surface(RadioGroup(onChanged: changes.Add)[RadioButton(key: "a"), RadioButton(key: "b")]);
        RadioGroupElement group = Assert.IsType<RadioGroupElement>(surface.RootElement);
        UiElement first = group.Children[0];
        Click(surface);
        Assert.Equal("a", group.SelectedKey);
        Click(surface);
        Assert.Single(changes);
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, false));
        Assert.Equal("b", group.SelectedKey);
        Assert.Same(group.Children[1], surface.FocusedElement);
        surface.Rebuild(RadioGroup(selectedKey: "b")[RadioButton(key: "b"), RadioButton(key: "a")]);
        surface.UpdateLayout();
        Assert.Same(first, group.Children[1]);
        Assert.Throws<ArgumentException>(() => surface.Rebuild(RadioGroup()[RadioButton(key: "a"), RadioButton(key: "a")]));
        Assert.Equal("b", group.SelectedKey);
        Assert.Equal(2, group.Children.Count);
        surface.Rebuild(RadioGroup(selectedKey: "b")[RadioButton(key: "a")]);
        surface.UpdateLayout();
        Assert.Null(group.SelectedKey);
        Assert.Equal(2, changes.Count);
    }

    /// <summary>Rejects wrong-parent radio before publication.</summary>
    [Fact]
    public void RadioRequiresDirectGroupAndNonemptyInheritedKey()
    {
        Assert.Throws<InvalidOperationException>(() => new UiSurface(RadioButton(key: "a")));
        Assert.Throws<InvalidOperationException>(() => new UiSurface(RadioGroup()[RadioButton()]));
    }

    /// <summary>Signal writeback is explicit and preserves the logical instance.</summary>
    [Fact]
    public void SignalModelUpdatesWithoutEchoAndRetainsElement()
    {
        var model = Signal.Create(false);
        int calls = 0;
        using UiSurface surface = Surface(Switch(isOn: model, onChanged: value =>
        {
            calls++;
            model.Value = value;
        }));
        UiElement element = surface.RootElement;
        Click(surface);
        surface.UpdateLayout();
        Assert.True(model.Value);
        Assert.Equal(1, calls);
        Assert.Same(element, surface.RootElement);
        model.Value = false;
        surface.UpdateLayout();
        Assert.False(Assert.IsType<SwitchElement>(element).IsOn);
        Assert.Equal(1, calls);
    }

    /// <summary>Modifiers preserve body actionability and never create a second mark target.</summary>
    [Fact]
    public void PaddingProbeAndContentShareOneSemanticOwner()
    {
        using UiSurface surface = Surface(CheckBox(with: [Padding.All(8)])[Rectangle(width: 20, height: 18)]);
        Assert.Null(surface.ProbePointer(new(2)).ActivationTarget);
        UiPointerProbe probe = surface.ProbePointer(new(34, 10));
        Assert.Equal(surface.RootElement.AutomationId, probe.ActivationTarget);
        Assert.Equal(2, surface.CaptureAutomationSnapshot().Nodes.Count);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(34, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(34, 10)));
        Assert.True(Assert.IsType<CheckBoxElement>(surface.RootElement).IsChecked);
    }

    /// <summary>Cancel, disabled and hidden publication end input without changing values.</summary>
    [Fact]
    public void CaptureOutsideCancelAndDisabledPublicationNeverCommit()
    {
        int calls = 0;
        using UiSurface surface = Surface(CheckBox(onChanged: _ => calls++));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(2)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(300)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(300)));
        Assert.False(surface.CaptureAutomationSnapshot().Nodes[0].IsChecked);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(2)));
        surface.Rebuild(CheckBox(isEnabled: false));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(2)));
        Assert.False(surface.CaptureAutomationSnapshot().Nodes[0].HasActivePress);
        Assert.Equal(0, calls);
        Assert.False(surface.Dispatch(new UiCheckInput(surface.RootElement.AutomationId, true, new(2))));
    }

    /// <summary>All radio siblings allow pointer focus but only one participates in Tab.</summary>
    [Fact]
    public void RadioRovingTabSkipsDisabledAndHiddenOptions()
    {
        using UiSurface surface = Surface(Stack()[RadioGroup()[RadioButton(key: "a"), RadioButton(key: "disabled", isEnabled: false), RadioButton(key: "hidden", isVisible: false), RadioButton(key: "b")], Button(testId: "after")]);
        surface.Dispatch(new UiKeyInput(Key.Tab, true));
        surface.Dispatch(new UiKeyInput(Key.Tab, false));
        RadioGroupElement group = Assert.IsType<RadioGroupElement>(surface.RootElement.Children[0]);
        Assert.Same(group.Children[0], surface.FocusedElement);
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, false));
        Assert.Equal("b", group.SelectedKey);
        surface.UpdateLayout();
        surface.Dispatch(new UiKeyInput(Key.Tab, true));
        surface.Dispatch(new UiKeyInput(Key.Tab, false));
        Assert.Same(surface.RootElement.Children[1], surface.FocusedElement);
    }

    /// <summary>Retained content reuses static marks and records new accepted values.</summary>
    [Fact]
    public void RetainedDrawingObservesAcceptedValueAndResize()
    {
        using UiSurface surface = Surface(CheckBox(threeState: true, with: [Padding.All(3)]));
        using UiDrawingScene2D drawing = surface.CreateDrawingScene();
        drawing.Update();
        drawing.Update();

        // Named background ordering uses the existing conservative opaque retained path.
        Assert.Equal(1, drawing.LastRecordingCount);
        var canvas = new Canvas2D();
        canvas.Begin();
        surface.Record(canvas);
        int uncheckedCount = canvas.End().Count;
        surface.Dispatch(new UiCheckInput(surface.RootElement.AutomationId, true, new(5)));
        drawing.Update();
        Assert.True(drawing.LastRecordingCount > 0);
        canvas.Begin();
        surface.Record(canvas);
        Assert.True(canvas.End().Count > uncheckedCount);
        surface.SetViewport(new(300, 40));
        drawing.Update();
        Assert.Equal(new Vector2(294, 34), surface.RootElement.ContentSize);
        Assert.True(surface.CaptureAutomationSnapshot().Nodes[0].IsChecked);
    }

    /// <summary>Desired checked input honors capabilities, geometry, occlusion and active ownership.</summary>
    [Fact]
    public void DesiredInputCannotSpoofRoleOrBypassOcclusionAndPress()
    {
        using UiSurface fake = Surface(Rectangle(role: UiRole.CheckBox));
        Assert.False(fake.Dispatch(new UiCheckInput(fake.RootElement.AutomationId, true, new(2))));
        using UiSurface surface = Surface(CheckBox());
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(2)));
        Assert.False(surface.Dispatch(new UiCheckInput(surface.RootElement.AutomationId, true, new(2))));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Cancel, new(2)));
        Assert.False(surface.Dispatch(new UiCheckInput(surface.RootElement.AutomationId, true, new(300))));
        Assert.False(surface.CaptureAutomationSnapshot().Nodes[0].IsChecked);
    }

    /// <summary>Key changes remove the old selection without callback and create a new radio lifetime.</summary>
    [Fact]
    public void RadioKeyReplacementClearsMissingSelectionWithoutCallback()
    {
        int calls = 0;
        using UiSurface surface = Surface(RadioGroup(selectedKey: "a", onChanged: _ => calls++)[RadioButton(key: "a")]);
        UiElement old = surface.RootElement.Children[0];
        surface.Rebuild(RadioGroup(selectedKey: "a", onChanged: _ => calls++)[RadioButton(key: "b")]);
        surface.UpdateLayout();
        Assert.Null(Assert.IsType<RadioGroupElement>(surface.RootElement).SelectedKey);
        Assert.NotSame(old, surface.RootElement.Children[0]);
        Assert.False(old.IsMounted);
        Assert.Equal(0, calls);
    }

    /// <summary>A two-state check accepts mixed externally and next activation commits true once.</summary>
    [Fact]
    public void TwoStateMixedAndExternalDeclarationNeverEcho()
    {
        int calls = 0;
        using UiSurface surface = Surface(CheckBox(isChecked: UiArgument<bool?>.Constant(null), onChanged: _ => calls++));
        Assert.Null(surface.CaptureAutomationSnapshot().Nodes[0].IsChecked);
        Click(surface);
        Assert.True(surface.CaptureAutomationSnapshot().Nodes[0].IsChecked);
        Assert.Equal(1, calls);
        surface.Rebuild(CheckBox(isChecked: false, onChanged: _ => calls++));
        surface.UpdateLayout();
        Assert.False(surface.CaptureAutomationSnapshot().Nodes[0].IsChecked);
        Assert.Equal(1, calls);
    }

    /// <summary>Projected wrong-parent failures preserve the published group and accepted selection.</summary>
    [Fact]
    public void WrongParentPrepareFailurePreservesOldTree()
    {
        using UiSurface surface = Surface(RadioGroup(selectedKey: "a")[RadioButton(key: "a")]);
        UiElement group = surface.RootElement;
        surface.Rebuild(Panel()[RadioButton(key: "a")]);
        Assert.Throws<InvalidOperationException>(() => surface.UpdateTree());
        Assert.Same(group, surface.RootElement);
        Assert.Equal("a", Assert.IsType<RadioGroupElement>(group).SelectedKey);
        Assert.True(group.Children[0].IsMounted);
    }

    /// <summary>Generated named slots retain one owner and default marks require no font provider.</summary>
    [Fact]
    public void NamedBackgroundAndExplicitStyleKeepOneOwnershipTree()
    {
        Compose.Definitions.CheckBox declaration = CheckBox(markSize: 12, gap: 4)[
            Rectangle(width: 20, height: 12),
            CheckBox.Background()[Rectangle(fill: new Lumyte.Mathematics.Colors.SRgba<float>(0, 0, 1, 1))]];
        using UiSurface surface = Surface(declaration);
        Assert.Equal(2, surface.RootElement.Children.Count);
        Assert.All(surface.RootElement.Children, child => Assert.Same(surface.RootElement, child.Parent));
        Assert.Equal(2, surface.CaptureAutomationSnapshot().Nodes.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(CheckBox(markSize: -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Switch(trackColor: new Lumyte.Mathematics.Colors.SRgba<float>(2, 0, 0, 1)));
    }

    /// <summary>Aspect-dependent mark capture never treats a differently sized destination as reusable.</summary>
    [Fact]
    public void MarkCaptureIsSupportedButDestinationReuseIsConservativelyRejected()
    {
        var widget = new Compose.Definitions.CheckBox();
        var probe = new CaptureProbe(widget);
        Assert.True(probe.SupportsCapture);
        Assert.False(probe.Reuses(widget));
        Assert.False(probe.Reuses(CheckBox(width: 300, height: 20)));
    }

    /// <summary>Named background radios never enter normal navigation.</summary>
    [Fact]
    public void RadioNavigationWrapsOnlyNormalChildren()
    {
        var changes = new List<string?>();
        using UiSurface surface = Surface(RadioGroup(selectedKey: "b", onChanged: changes.Add)[
            RadioButton(key: "a"),
            RadioButton(key: "b"),
            RadioGroup.Background()[RadioButton(key: "decoration")]]);
        RadioGroupElement group = Assert.IsType<RadioGroupElement>(surface.RootElement);
        Assert.True(surface.TryFocus(group.Children.Single(child => child.Widget.Key == "b")));
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowRight, false));
        Assert.Equal("a", group.SelectedKey);
        Assert.Equal("a", surface.FocusedElement!.Widget.Key);
        surface.Dispatch(new UiKeyInput(Key.ArrowLeft, true));
        surface.Dispatch(new UiKeyInput(Key.ArrowLeft, false));
        Assert.Equal("b", group.SelectedKey);
        Assert.Equal("b", surface.FocusedElement!.Widget.Key);
        Assert.Equal(new[] { "a", "b" }, changes);
        Assert.DoesNotContain(surface.CaptureAutomationSnapshot().Nodes, node => node.OptionKey == "decoration");
    }

    private static UiSurface Surface(Lumyte.UI.Compose.Definitions.Widget widget)
    {
        var surface = new UiSurface(widget);
        surface.SetViewport(new(200, 160));
        surface.UpdateLayout();
        return surface;
    }

    private static void Click(UiSurface surface)
    {
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(2)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(2)));
    }

    private sealed class CaptureProbe(Compose.Definitions.CheckBox widget) : CheckBoxElement(widget)
    {
        internal bool SupportsCapture => SupportsTransitionCapture;

        internal bool Reuses(Lumyte.UI.Compose.Definitions.Widget other) => CanReuseTransitionCapture(other);
    }
}
