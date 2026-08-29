using Lumyte.Composition;

namespace Lumyte.Interaction;

[Composable(Factory = "InteractionKit", Name = "KeybindingMap")]
public sealed partial class KeybindingMap
{
    private Keybinding[] bindings = [];

    [ComposeParameter]
    public required string Name { get; init; }

    public IReadOnlyList<Keybinding> Bindings => bindings;

    [ComposeContent]
    private IReadOnlyList<Keybinding> ComposedBindings
    {
        get => bindings;
        set => bindings = [.. value];
    }
}
