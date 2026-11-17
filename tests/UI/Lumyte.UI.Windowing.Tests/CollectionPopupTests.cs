using Lumyte.Input.Abstractions;
using Lumyte.UI.Collections;
using Lumyte.UI.Controls.Windowing;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Uses the actual hosted popup manager and ordinary routed input.</summary>
public sealed class CollectionPopupTests
{
    /// <summary>Cancel does not select, commit occurs once, and reopening constructs fresh content.</summary>
    [Fact]
    public void ComboUsesPopupScopeAndRestoresFocus()
    {
        using var fixture = new Fixture();
        int changes = 0;
        int templates = 0;
        var host = new CollectionPopupHost(fixture.Popups, fixture.Owner);
        Lumyte.UI.Compose.Definitions.Widget Template(string item)
        {
            templates++;
            return SizedBox(width: 80, height: 20);
        }

        fixture.Surface.Rebuild(ComboBox<string>(items: new[] { "a", "b" }, keySelector: item => item, itemTemplate: Template, selectedItemTemplate: _ => SizedBox(width: 80, height: 20), emptyTemplate: () => SizedBox(width: 80, height: 20), popupHost: host, selectedKey: "a", onSelectionChanged: _ => changes++, width: 80, height: 20));
        fixture.Surface.UpdateLayout();
        Assert.True(fixture.Surface.TryFocus(fixture.Surface.RootElement));
        fixture.Send(new UiKeyInput(Key.ArrowDown, true));
        fixture.Send(new UiKeyInput(Key.ArrowDown, false));
        fixture.Pump();
        UiPopupInfo first = Assert.Single(fixture.Popups.CapturePopups());
        Assert.Equal(2, templates);
        Assert.Null(fixture.Popups.ProbePointer(fixture.Surface, new(5)).ActivationTarget);
        fixture.Send(new UiKeyInput(Key.Escape, true));
        fixture.Send(new UiKeyInput(Key.Escape, false));
        fixture.Pump();
        Assert.Empty(fixture.Popups.CapturePopups());
        Assert.Equal(0, changes);
        Assert.Same(fixture.Surface.RootElement, fixture.Surface.FocusedElement);
        fixture.Send(new UiKeyInput(Key.ArrowDown, true));
        fixture.Send(new UiKeyInput(Key.ArrowDown, false));
        fixture.Pump();
        Assert.NotEqual(first.Id, Assert.Single(fixture.Popups.CapturePopups()).Id);
        Assert.Equal(4, templates);
        fixture.Send(new UiKeyInput(Key.ArrowDown, true));
        fixture.Send(new UiKeyInput(Key.ArrowDown, false));
        fixture.Send(new UiKeyInput(Key.Enter, true));
        fixture.Send(new UiKeyInput(Key.Enter, false));
        fixture.Pump();
        Assert.Equal("b", Assert.IsType<ComboBoxElement<string>>(fixture.Surface.RootElement).SelectedKey);
        Assert.Equal(1, changes);
        Assert.Empty(fixture.Popups.CapturePopups());
    }

    /// <summary>A second owner click cancels the open popup without reopening or selecting.</summary>
    [Fact]
    public void RepeatedOwnerClickClosesWithoutSelection()
    {
        using var fixture = new Fixture();
        int changes = 0;
        var host = new CollectionPopupHost(fixture.Popups, fixture.Owner);
        fixture.Surface.Rebuild(ComboBox<string>(items: new[] { "a", "b" }, keySelector: item => item, itemTemplate: _ => SizedBox(width: 80, height: 20), selectedItemTemplate: _ => SizedBox(width: 80, height: 20), emptyTemplate: () => SizedBox(width: 80, height: 20), popupHost: host, selectedKey: "a", onSelectionChanged: _ => changes++, width: 80, height: 20));
        fixture.Surface.UpdateLayout();
        fixture.Send(new UiPointerInput(1, UiPointerPhase.Down, new(5)));
        fixture.Send(new UiPointerInput(1, UiPointerPhase.Up, new(5)));
        fixture.Pump();
        Assert.Single(fixture.Popups.CapturePopups());
        fixture.Send(new UiPointerInput(1, UiPointerPhase.Down, new(5)));
        fixture.Send(new UiPointerInput(1, UiPointerPhase.Up, new(5)));
        fixture.Pump();
        Assert.Empty(fixture.Popups.CapturePopups());
        Assert.False(Assert.IsType<ComboBoxElement<string>>(fixture.Surface.RootElement).IsExpanded);
        Assert.Equal("a", Assert.IsType<ComboBoxElement<string>>(fixture.Surface.RootElement).SelectedKey);
        Assert.Equal(0, changes);
    }

    /// <summary>Outside press cancels and declarative replacement rejects an old popup gesture.</summary>
    [Fact]
    public void OutsideAndReplacementCancelWithoutSelectionCallbacks()
    {
        using var fixture = new Fixture();
        int changes = 0;
        var host = new CollectionPopupHost(fixture.Popups, fixture.Owner);
        Lumyte.UI.Collections.Compose.Definitions.ComboBox<string> Build(string[] items) => ComboBox<string>(items: items, keySelector: item => item, itemTemplate: _ => SizedBox(width: 80, height: 20), selectedItemTemplate: _ => SizedBox(width: 80, height: 20), emptyTemplate: () => SizedBox(width: 80, height: 20), popupHost: host, selectedKey: "a", onSelectionChanged: _ => changes++);
        fixture.Surface.Rebuild(Build(["a", "b"]));
        fixture.Surface.UpdateLayout();
        fixture.Surface.TryFocus(fixture.Surface.RootElement);
        fixture.Send(new UiKeyInput(Key.ArrowDown, true));
        fixture.Send(new UiKeyInput(Key.ArrowDown, false));
        fixture.Pump();
        fixture.Popups.NotifyOutsidePress(fixture.Owner);
        fixture.Pump();
        Assert.Equal(0, changes);
        Assert.Empty(fixture.Popups.CapturePopups());
        fixture.Send(new UiKeyInput(Key.ArrowDown, true));
        fixture.Send(new UiKeyInput(Key.ArrowDown, false));
        fixture.Pump();
        fixture.Surface.Rebuild(Build(["c"]));
        fixture.Surface.UpdateLayout();
        fixture.Pump();
        Assert.Empty(fixture.Popups.CapturePopups());
        Assert.Null(Assert.IsType<ComboBoxElement<string>>(fixture.Surface.RootElement).SelectedKey);
        Assert.Equal(0, changes);
    }

    /// <summary>Factory failure ends the request without callbacks and permits a new opening.</summary>
    [Fact]
    public void PopupItemFactoryFailureLeavesSelectionAndCanReopen()
    {
        using var fixture = new Fixture();
        bool fail = true;
        int changes = 0;
        var host = new CollectionPopupHost(fixture.Popups, fixture.Owner);
        Lumyte.UI.Compose.Definitions.Widget Template(string item) => fail ? throw new InvalidOperationException("template") : SizedBox(height: 20);
        fixture.Surface.Rebuild(ComboBox<string>(items: new[] { "a" }, keySelector: item => item, itemTemplate: Template, selectedItemTemplate: _ => SizedBox(height: 20), emptyTemplate: () => SizedBox(height: 20), popupHost: host, selectedKey: "a", onSelectionChanged: _ => changes++));
        fixture.Surface.UpdateLayout();
        fixture.Surface.TryFocus(fixture.Surface.RootElement);
        fixture.Send(new UiKeyInput(Key.ArrowDown, true));
        fixture.Send(new UiKeyInput(Key.ArrowDown, false));
        fixture.Pump();
        Assert.Empty(fixture.Popups.CapturePopups());
        Assert.False(Assert.IsType<ComboBoxElement<string>>(fixture.Surface.RootElement).IsExpanded);
        Assert.Equal(0, changes);
        fail = false;
        fixture.Send(new UiKeyInput(Key.ArrowDown, true));
        fixture.Send(new UiKeyInput(Key.ArrowDown, false));
        fixture.Pump();
        Assert.Single(fixture.Popups.CapturePopups());
        Assert.Equal("a", Assert.IsType<ComboBoxElement<string>>(fixture.Surface.RootElement).SelectedKey);
    }

    /// <summary>Selection is accepted and popup closed before a throwing latest callback runs.</summary>
    [Fact]
    public void LatestComboCallbackObservesAcceptedSelectionOnFailure()
    {
        using var fixture = new Fixture();
        int calls = 0;
        var host = new CollectionPopupHost(fixture.Popups, fixture.Owner);
        Lumyte.UI.Collections.Compose.Definitions.ComboBox<string> declaration = ComboBox<string>(items: new[] { "a", "b" }, keySelector: item => item, itemTemplate: _ => SizedBox(height: 20), selectedItemTemplate: _ => SizedBox(height: 20), emptyTemplate: () => SizedBox(height: 20), popupHost: host, selectedKey: "a");
        fixture.Surface.Rebuild(declaration);
        fixture.Surface.UpdateLayout();
        fixture.Surface.TryFocus(fixture.Surface.RootElement);
        fixture.Send(new UiKeyInput(Key.ArrowDown, true));
        fixture.Send(new UiKeyInput(Key.ArrowDown, false));
        fixture.Pump();
        declaration.OnSelectionChanged = key =>
        {
            calls++;
            Assert.Equal(key, Assert.IsType<ComboBoxElement<string>>(fixture.Surface.RootElement).SelectedKey);
            Assert.False(Assert.IsType<ComboBoxElement<string>>(fixture.Surface.RootElement).IsExpanded);
            throw new InvalidOperationException("callback");
        };
        fixture.Send(new UiKeyInput(Key.ArrowDown, true));
        fixture.Send(new UiKeyInput(Key.ArrowDown, false));
        fixture.Send(new UiKeyInput(Key.Enter, true));
        Assert.Throws<InvalidOperationException>(() => fixture.Send(new UiKeyInput(Key.Enter, false)));
        fixture.Pump();
        Assert.Equal(1, calls);
        Assert.Equal("b", Assert.IsType<ComboBoxElement<string>>(fixture.Surface.RootElement).SelectedKey);
    }
}
