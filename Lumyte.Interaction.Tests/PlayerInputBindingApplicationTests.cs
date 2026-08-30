using System.Text.Json;

using Lumyte.Input;

using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class PlayerInputBindingApplicationTests
{
    [Fact]
    public void JsonOverrideRebindsTheRunningPlayerInput()
    {
        var jump = new InputAction<bool>("game.jump");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        string json = CreateOverrideJson(map, Key.J);
        var keyboard = new VirtualKeyboard();
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(platform, [map]);
        PlayerInput player = inputs.AddPlayer(new() { Player = 0 });
        inputs.Assign(keyboard, player);
        var phases = new List<ActionPhase>();
        player.Actions.PhaseChanged += (_, eventArgs) => phases.Add(eventArgs.Phase);
        keyboard.Press(Key.Space);

        player.ApplyBindingOverrides(json);
        bool afterApply = player.Actions.GetValue(jump);
        keyboard.Release(Key.Space);
        keyboard.Press(Key.J);

        Assert.False(afterApply);
        Assert.True(player.Actions.GetValue(jump));
        Assert.Contains(ActionPhase.Canceled, phases);
        Assert.Equal(InputControlDescriptor.From(InputControls.Key(Key.J)),
            Assert.Single(player.Bindings.Slots).Control);
    }

    [Fact]
    public void InitialJsonOverrideIsActiveWhenThePlayerIsCreated()
    {
        var jump = new InputAction<bool>("game.jump");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        string json = CreateOverrideJson(map, Key.J);
        var keyboard = new VirtualKeyboard();
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(platform, [map]);
        PlayerInput player = inputs.AddPlayer(new()
        {
            Player = 0,
            BindingOverridesJson = json,
        });
        inputs.Assign(keyboard, player);

        keyboard.Press(Key.Space);
        bool space = player.Actions.GetValue(jump);
        keyboard.Release(Key.Space);
        keyboard.Press(Key.J);

        Assert.False(space);
        Assert.True(player.Actions.GetValue(jump));
    }

    [Fact]
    public void InvalidJsonLeavesTheRunningBindingsUnchanged()
    {
        var jump = new InputAction<bool>("game.jump");
        ActionMap map = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space))
        ];
        var keyboard = new VirtualKeyboard();
        var platform = new VirtualPlatformInput();
        using var inputs = new PlayerInputManager(platform, [map]);
        PlayerInput player = inputs.AddPlayer(new() { Player = 0 });
        inputs.Assign(keyboard, player);

        Assert.Throws<JsonException>(() => player.ApplyBindingOverrides("not json"));
        keyboard.Press(Key.Space);

        Assert.True(player.Actions.GetValue(jump));
    }

    private static string CreateOverrideJson(ActionMap map, Key key)
    {
        var document = ActionBindingDocument.Create([map]);
        RebindingSession session = document.BeginRebinding(Assert.Single(document.Slots).Id);
        session.TryOffer(RebindingCandidate.From(InputControls.Key(key)));
        session.Confirm();
        return document.SaveOverrides();
    }
}
