using Lumyte.Input;
using Xunit;

using static Lumyte.Interaction.InteractionKit;

namespace Lumyte.Interaction.Tests;

public sealed class ActionRuntimeSnapshotTests
{
    [Fact]
    public void SnapshotDescribesBindingsAndCurrentActionState()
    {
        var keyboard = new TestKeyboard();
        var jump = new InputAction<bool>("game.jump");
        ActionMap map = ActionMap("Gameplay")[new ActionBinding<bool>(jump, InputControls.Key(Key.Space)) { BindingId = "jump-key" }];
        using var runtime = new ActionRuntime(keyboard, new InteractionContext(), map);
        keyboard.Press(Key.Space);

        ActionRuntimeSnapshot snapshot = runtime.GetSnapshot();

        ActionMapSnapshot mapSnapshot = Assert.Single(snapshot.Maps);
        Assert.Equal("Gameplay", mapSnapshot.Name);
        Assert.Equal("jump-key", Assert.Single(mapSnapshot.Bindings).BindingId);
        ActionStateSnapshot action = Assert.Single(snapshot.Actions);
        Assert.Equal(("game.jump", true, ActionPhase.Performed), (action.Id, action.Value, action.Phase));
    }

    private sealed class TestKeyboard : IKeyboard
    {
        private readonly HashSet<Key> pressed = [];
        public event EventHandler<KeyChangedEventArgs>? KeyChanged;
        public bool IsKeyPressed(Key key) => pressed.Contains(key);
        public void Press(Key key) { pressed.Add(key); KeyChanged?.Invoke(this, new(key, true, false)); }
    }
}
