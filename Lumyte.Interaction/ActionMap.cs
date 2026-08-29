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

    internal static ActionMap CreateEffective(
        string name,
        ContextCondition when,
        int priority,
        IReadOnlyList<ActionBinding> bindings)
    {
        ActionMap map = InteractionKit.ActionMap(name, when, priority);
        return map[bindings.ToArray()];
    }
}
