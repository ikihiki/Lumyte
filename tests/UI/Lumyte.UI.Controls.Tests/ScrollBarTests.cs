using Lumyte.Input.Abstractions;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;

namespace Lumyte.UI.Controls.Tests;

/// <summary>Verifies a borrowed scrollbar uses ordinary capture and keyboard input.</summary>
public sealed class ScrollBarTests
{
    /// <summary>Checks canceled thumb releases without an offset callback and can be acquired again.</summary>
    [Fact]
    public void CancelDoesNotChangeOffsetOrNotify()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(Grid(columns: new UiGridTrack[] { new(100), new(20) })[
            ScrollView(controller: controller, with: [Grid.Cell()])[SizedBox(height: 500)],
            ScrollBar(controller: controller, with: [Grid.Cell(column: 1)])]);
        surface.SetViewport(new(120, 100));
        surface.UpdateLayout();
        int changes = 0;
        controller.OnChanged = _ => changes++;
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(110, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Cancel, default));
        Assert.Equal(0, changes);
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(110, 10)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Move, new(110, 30)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Up, new(110, 30)));
        Assert.Equal(1, changes);
        Assert.Equal(100, controller.Offset);
    }

    /// <summary>Checks drag anchoring, terminal release and keyboard range endpoints.</summary>
    [Fact]
    public void DragAndKeyboardUseSharedController()
    {
        using var controller = new ScrollController();
        using var surface = new UiSurface(Grid(columns: new UiGridTrack[] { new(100), new(20) })[
            ScrollView(controller: controller, with: [Grid.Cell()])[SizedBox(height: 500)],
            ScrollBar(controller: controller, with: [Grid.Cell(column: 1)])]);
        surface.SetViewport(new(120, 100));
        surface.UpdateLayout();
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(110, 10))));
        Assert.True(surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(110, 50))));
        Assert.Equal(200, controller.Offset);
        UiAutomationNode bar = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.Role == UiRole.ScrollBar);
        Assert.Equal(0.5, bar.Value);
        Assert.Equal(UiAutomationActions.Focus, bar.Actions);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(110, 50)));
        surface.Dispatch(new UiKeyInput(Key.End, true));
        surface.Dispatch(new UiKeyInput(Key.End, false));
        Assert.Equal(400, controller.Offset);
        surface.Dispatch(new UiKeyInput(Key.Home, true));
        surface.Dispatch(new UiKeyInput(Key.Home, false));
        Assert.Equal(0, controller.Offset);
        Assert.Equal(4, surface.CaptureAutomationSnapshot().Nodes.Count);
    }
}
