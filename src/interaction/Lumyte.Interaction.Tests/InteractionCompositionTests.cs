using Lumyte.Input;

using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class InteractionCompositionTests
{
    [Fact]
    public void ActionMapComposesTypedBindingsInDeclarationOrder()
    {
        var jump = new InputAction<bool>("game.jump");
        var keyboard = new ActionBinding<bool>(jump, InputControls.Key(Key.Space));
        var gamepad = new ActionBinding<bool>(jump, InputControls.GamepadButton(GamepadButtons.South));

        ActionMap map = ActionMap("Gameplay")[keyboard, gamepad];

        Assert.Equal("Gameplay", map.Name);
        Assert.Equal([keyboard, gamepad], map.Bindings);
    }

    [Fact]
    public void GestureMapCanTargetAnEditorCommand()
    {
        var pan = new Command("editor.panViewport");
        var drag = new DragGesture(pan, minimumDistance: 3);

        GestureMap map = GestureMap("Viewport")[drag];

        GestureBinding actual = Assert.Single(map.Bindings);
        Assert.Same(pan, actual.Intent);
        Assert.Equal(GestureKind.Drag, actual.Kind);
        Assert.Equal(3, Assert.IsType<DragGesture>(actual).MinimumDistance);
    }

    [Fact]
    public void KeybindingMapKeepsCommandsSeparateFromTheirChords()
    {
        var save = new Command("editor.save");
        var binding = new Keybinding(
            save,
            new KeyChord(new KeyStroke(Key.S, ModifierKeys.Control)));

        KeybindingMap map = KeybindingMap("Editor")[binding];

        Keybinding actual = Assert.Single(map.Bindings);
        Assert.Same(save, actual.Command);
        Assert.Equal(Key.S, Assert.Single(actual.Chord.Strokes).Key);
    }
}
