using Lumyte.UI.TextEditing;
using Xunit;
using static Lumyte.UI.Controls.Compose;

namespace Lumyte.UI.Controls.Tests;

/// <summary>Checks controls and editor content use one surface scope without duplicate registrations.</summary>
public sealed class TextInputBoundaryTests
{
    /// <summary>Editor input is consumed once and never toggles the enclosing control.</summary>
    [Fact]
    public void EditorContentOwnsPointerBeforeOuterToggle()
    {
        int changes = 0;
        using var surface = new UiSurface(CheckBox(onChanged: _ => changes++)[
            Lumyte.UI.TextEditing.Compose.TextBox(width: 50, height: 20)]);
        surface.SetViewport(new(100, 40));
        surface.UpdateLayout();
        var handler = new Handler();
        using IDisposable registration = UiTextInputScope.Attach(surface, handler);
        Assert.Throws<ArgumentException>(() => UiTextInputScope.Attach(surface, handler));
        var point = new System.Numerics.Vector2(30, 5);
        Assert.Null(surface.ProbePointer(point).ActivationTarget);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, point));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, point));
        Assert.Equal(2, handler.Deliveries);
        Assert.Equal(0, changes);
        Assert.False(Assert.IsType<CheckBoxElement>(surface.RootElement).IsChecked);
    }

    private sealed class Handler : IUiTextInputHandler
    {
        internal int Deliveries { get; private set; }

        public UiInputResult HandleInput(TextBoxElement editor, in UiInputContext context)
        {
            Deliveries++;
            return UiInputResult.Handled;
        }

        public void OnPointerCaptureChanged(TextBoxElement editor, UiPointerCaptureState state)
        {
        }
    }
}
