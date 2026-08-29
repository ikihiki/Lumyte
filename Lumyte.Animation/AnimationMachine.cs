using Lumyte.Composition;
using Lumyte.StateMachine;

namespace Lumyte.Animation;

[Composable(Factory = "AnimationKit", Name = "AnimationMachine")]
public sealed partial class AnimationMachine<TContext, TTrigger>
{
    private IAnimationMachinePart[] parts = [];
    private StateMachine<TContext, TTrigger>? definition;
    private AnimationStateMachineBinding<TContext, TTrigger>? binding;

    [ComposeParameter]
    public required State<TContext> InitialState { get; init; }

    [ComposeContent]
    private IReadOnlyList<IAnimationMachinePart> ComposedParts
    {
        get => parts;
        set
        {
            if (definition is not null)
            {
                throw new InvalidOperationException("An animation machine can be composed only once.");
            }

            parts = [.. value];
            Build();
        }
    }

    public AnimationMachineInstance<TContext, TTrigger> CreateInstance(
        TContext context,
        AnimationPlayer player,
        AnimationTarget target)
    {
        EnsureComposed();
        return new AnimationMachineInstance<TContext, TTrigger>(
            definition!.CreateInstance(context),
            binding!,
            player,
            target);
    }

    private void Build()
    {
        AnimationStatePart<TContext>[] stateParts =
        [
            .. parts.OfType<AnimationStatePart<TContext>>(),
        ];
        AnimationTransitionPart<TContext, TTrigger>[] transitionParts =
        [
            .. parts.OfType<AnimationTransitionPart<TContext, TTrigger>>(),
        ];
        if (stateParts.Length + transitionParts.Length != parts.Length)
        {
            throw new ArgumentException(
                "Every animation machine part must use the machine's context and trigger types.",
                nameof(ComposedParts));
        }

        if (stateParts.Select(part => part.State).Distinct().Count() != stateParts.Length)
        {
            throw new ArgumentException(
                "An animation state can have only one timeline binding.",
                nameof(ComposedParts));
        }

        State<TContext>[] requiredStates =
        [
            InitialState,
            .. transitionParts.SelectMany(part => new[] { part.Transition.From, part.Transition.To }),
        ];
        HashSet<State<TContext>> boundStates = [.. stateParts.Select(part => part.State)];
        State<TContext>? missing = requiredStates.FirstOrDefault(state => !boundStates.Contains(state));
        if (missing is not null)
        {
            throw new ArgumentException(
                $"Animation state '{missing.Name}' has no timeline binding.",
                nameof(ComposedParts));
        }

        Transition<TContext, TTrigger>[] transitions =
        [
            .. transitionParts.Select(part => part.Transition),
        ];
        definition = StateMachineKit.Machine<TContext, TTrigger>(InitialState)[transitions];
        binding = new AnimationStateMachineBinding<TContext, TTrigger>();
        foreach (AnimationStatePart<TContext> part in stateParts)
        {
            binding.Bind(part.State, part.Timeline);
        }

        foreach (AnimationTransitionPart<TContext, TTrigger> part in transitionParts)
        {
            part.Freeze();
            binding.Bind(part.Transition, part.Animation);
        }
    }

    private void EnsureComposed()
    {
        if (definition is null)
        {
            throw new InvalidOperationException(
                "An animation machine must be composed before an instance can be created.");
        }
    }
}
