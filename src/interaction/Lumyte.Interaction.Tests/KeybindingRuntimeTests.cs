using Lumyte.Core.Time;
using Lumyte.Input;

using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class KeybindingRuntimeTests
{
    [Fact]
    public void EmulatedModifiedKeyInvokesTheBoundCommand()
    {
        var save = new Command("editor.save");
        KeybindingMap map = KeybindingMap("Editor")[
            new Keybinding(save, KeyChordParser.Parse("ctrl+s"))
        ];
        var keyboard = new VirtualKeyboard();
        using KeybindingRuntime runtime = CreateRuntime(keyboard, map);
        Command? invoked = null;
        runtime.CommandInvoked += (_, eventArgs) => invoked = eventArgs.Command;

        keyboard.Press(Key.LeftControl);
        keyboard.Press(Key.S);

        Assert.Same(save, invoked);
    }

    [Fact]
    public void EmulatedChordInvokesAfterItsFinalStroke()
    {
        var comment = new Command("editor.comment");
        KeybindingMap map = KeybindingMap("Editor")[
            new Keybinding(comment, KeyChordParser.Parse("ctrl+k ctrl+c"))
        ];
        var keyboard = new VirtualKeyboard();
        using KeybindingRuntime runtime = CreateRuntime(keyboard, map);
        var invoked = new List<Command>();
        runtime.CommandInvoked += (_, eventArgs) => invoked.Add(eventArgs.Command);

        keyboard.Press(Key.LeftControl);
        keyboard.Press(Key.K);
        keyboard.Release(Key.K);
        Assert.Empty(invoked);
        keyboard.Press(Key.C);

        Assert.Equal([comment], invoked);
        Assert.False(runtime.IsChordPending);
    }

    [Fact]
    public void InactiveWhenConditionPreventsInvocation()
    {
        var textInputFocused = ContextKey.Create<bool>("ui.textInputFocused");
        var save = new Command("editor.save");
        KeybindingMap map = KeybindingMap("Editor")[
            new Keybinding(save, KeyChordParser.Parse("ctrl+s"))
            {
                When = textInputFocused.IsNot(true),
            }
        ];
        var keyboard = new VirtualKeyboard();
        var context = new InteractionContext();
        context.Set(textInputFocused, true);
        using var runtime = new KeybindingRuntime(
            keyboard,
            context,
            new ManualClock(),
            map);
        var invoked = new List<Command>();
        runtime.CommandInvoked += (_, eventArgs) => invoked.Add(eventArgs.Command);

        keyboard.Press(Key.LeftControl);
        keyboard.Press(Key.S);

        Assert.Empty(invoked);
    }

    [Fact]
    public void ShorterChordWaitsUntilTheLongerChordTimesOut()
    {
        var prefix = new Command("editor.prefix");
        var longer = new Command("editor.longer");
        KeybindingMap map = KeybindingMap("Editor")[
            new Keybinding(prefix, KeyChordParser.Parse("ctrl+k")),
            new Keybinding(longer, KeyChordParser.Parse("ctrl+k ctrl+c"))
        ];
        var keyboard = new VirtualKeyboard();
        var clock = new ManualClock();
        using var runtime = new KeybindingRuntime(
            keyboard,
            new InteractionContext(),
            clock,
            map,
            Duration.FromSeconds(1));
        var invoked = new List<Command>();
        runtime.CommandInvoked += (_, eventArgs) => invoked.Add(eventArgs.Command);

        keyboard.Press(Key.LeftControl);
        keyboard.Press(Key.K);
        clock.Advance(Duration.FromSeconds(1));
        runtime.Update();

        Assert.Equal([prefix], invoked);
        Assert.False(runtime.IsChordPending);
    }

    [Fact]
    public void RepeatedKeyDoesNotInvokeACommandAgain()
    {
        var save = new Command("editor.save");
        KeybindingMap map = KeybindingMap("Editor")[
            new Keybinding(save, KeyChordParser.Parse("ctrl+s"))
        ];
        var keyboard = new VirtualKeyboard();
        using KeybindingRuntime runtime = CreateRuntime(keyboard, map);
        var invoked = new List<Command>();
        runtime.CommandInvoked += (_, eventArgs) => invoked.Add(eventArgs.Command);

        keyboard.Press(Key.LeftControl);
        keyboard.Press(Key.S);
        keyboard.Press(Key.S);

        Assert.Equal([save], invoked);
    }

    [Fact]
    public void LongerChordWinsWhenCompletedBeforeTheTimeout()
    {
        var prefix = new Command("editor.prefix");
        var longer = new Command("editor.longer");
        KeybindingMap map = KeybindingMap("Editor")[
            new Keybinding(prefix, KeyChordParser.Parse("ctrl+k")),
            new Keybinding(longer, KeyChordParser.Parse("ctrl+k ctrl+c"))
        ];
        var keyboard = new VirtualKeyboard();
        using KeybindingRuntime runtime = CreateRuntime(keyboard, map);
        var invoked = new List<Command>();
        runtime.CommandInvoked += (_, eventArgs) => invoked.Add(eventArgs.Command);

        keyboard.Press(Key.LeftControl);
        keyboard.Press(Key.K);
        keyboard.Release(Key.K);
        keyboard.Press(Key.C);

        Assert.Equal([longer], invoked);
    }

    [Fact]
    public void UnrelatedStrokeCancelsAPendingChord()
    {
        var comment = new Command("editor.comment");
        KeybindingMap map = KeybindingMap("Editor")[
            new Keybinding(comment, KeyChordParser.Parse("ctrl+k ctrl+c"))
        ];
        var keyboard = new VirtualKeyboard();
        using KeybindingRuntime runtime = CreateRuntime(keyboard, map);
        var invoked = new List<Command>();
        runtime.CommandInvoked += (_, eventArgs) => invoked.Add(eventArgs.Command);

        keyboard.Press(Key.LeftControl);
        keyboard.Press(Key.K);
        keyboard.Release(Key.K);
        keyboard.Press(Key.X);

        Assert.Empty(invoked);
        Assert.False(runtime.IsChordPending);
    }

    private static KeybindingRuntime CreateRuntime(
        VirtualKeyboard keyboard,
        KeybindingMap map) =>
        new(keyboard, new InteractionContext(), new ManualClock(), map);
}
