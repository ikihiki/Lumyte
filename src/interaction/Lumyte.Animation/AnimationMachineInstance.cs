using Lumyte.StateMachine;

namespace Lumyte.Animation;

public sealed class AnimationMachineInstance<TContext, TTrigger>
{
    private readonly AnimationStateMachineController<TContext, TTrigger> controller;

    internal AnimationMachineInstance(
        StateMachineInstance<TContext, TTrigger> machine,
        AnimationStateMachineBinding<TContext, TTrigger> binding,
        AnimationPlayer player,
        AnimationTarget target)
    {
        controller = new AnimationStateMachineController<TContext, TTrigger>(
            machine,
            binding,
            player,
            target);
    }

    public State<TContext> CurrentState => controller.CurrentState;

    public PlaybackHandle Start(PlaybackOptions? options = null) =>
        controller.Start(options);

    public bool Fire(TTrigger trigger) => controller.Fire(trigger);
}
