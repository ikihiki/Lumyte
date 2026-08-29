using Lumyte.Composition;

namespace Lumyte.Interaction;

[Composable(Factory = "InteractionKit", Name = "ActionMap")]
public sealed partial class ActionMap
{
    private ActionBinding[] bindings = [];

    [ComposeParameter]
    public required string Name { get; init; }

    [ComposeParameter]
    public ContextCondition When { get; init; } = ContextCondition.Always;

    [ComposeParameter]
    public int Priority { get; init; }

    public IReadOnlyList<ActionBinding> Bindings => bindings;

    [ComposeContent]
    private IReadOnlyList<ActionBinding> ComposedBindings
    {
        get => bindings;
        set => bindings = [.. value];
    }
}
