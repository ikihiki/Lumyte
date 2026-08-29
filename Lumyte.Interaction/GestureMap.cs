using Lumyte.Composition;

namespace Lumyte.Interaction;

[Composable(Factory = "InteractionKit", Name = "GestureMap")]
public sealed partial class GestureMap
{
    private GestureBinding[] bindings = [];

    [ComposeParameter]
    public required string Name { get; init; }

    [ComposeParameter]
    public ContextCondition When { get; init; } = ContextCondition.Always;

    [ComposeParameter]
    public int Priority { get; init; }

    public IReadOnlyList<GestureBinding> Bindings => bindings;

    [ComposeContent]
    private IReadOnlyList<GestureBinding> ComposedBindings
    {
        get => bindings;
        set => bindings = [.. value];
    }
}
