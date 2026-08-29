using Lumyte.Input;

using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class ActionRuntimeTests
{
    [Fact]
    public void EmulatedKeyPressAndReleaseUpdateTheBoundAction()
    {
        var jump = new InputAction<bool>("game.jump");
        ActionMap gameplay = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        var keyboard = new VirtualKeyboard();
        using var runtime = new ActionRuntime(keyboard, new InteractionContext(), gameplay);
        var changes = new List<bool>();
        runtime.ActionChanged += (_, eventArgs) => changes.Add(eventArgs.Value);

        keyboard.Press(Key.Space);
        bool pressed = runtime.GetValue(jump);
        keyboard.Release(Key.Space);
        bool released = runtime.GetValue(jump);

        Assert.True(pressed);
        Assert.False(released);
        Assert.Equal([true, false], changes);
    }

    [Fact]
    public void EmulatedInputOnlyUsesMapsWhoseContextIsActive()
    {
        ContextKey<bool> gameRunning = ContextKey.Create<bool>("game.running");
        var jump = new InputAction<bool>("game.jump");
        ActionMap gameplay = ActionMap("Gameplay", gameRunning.Is(true))[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        var context = new InteractionContext();
        var keyboard = new VirtualKeyboard();
        using var runtime = new ActionRuntime(keyboard, context, gameplay);

        keyboard.Press(Key.Space);
        bool whileDisabled = runtime.GetValue(jump);
        keyboard.Release(Key.Space);
        context.Set(gameRunning, true);
        keyboard.Press(Key.Space);
        bool whileEnabled = runtime.GetValue(jump);

        Assert.False(whileDisabled);
        Assert.True(whileEnabled);
    }

    [Fact]
    public void HigherPriorityMapReceivesEmulatedInput()
    {
        var gameplayAction = new InputAction<bool>("game.confirm");
        var menuAction = new InputAction<bool>("menu.confirm");
        ActionMap gameplay = ActionMap("Gameplay", priority: 10)[
            new ActionBinding<bool>(gameplayAction, InputControls.Key(Key.Enter))
        ];
        ActionMap menu = ActionMap("Menu", priority: 100)[
            new ActionBinding<bool>(menuAction, InputControls.Key(Key.Enter))
        ];
        var keyboard = new VirtualKeyboard();
        using var runtime = new ActionRuntime(
            keyboard,
            new InteractionContext(),
            gameplay,
            menu);

        keyboard.Press(Key.Enter);

        Assert.False(runtime.GetValue(gameplayAction));
        Assert.True(runtime.GetValue(menuAction));
    }
}
