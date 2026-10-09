using Lumyte.Composition;
using Lumyte.StateMachines;

namespace Lumyte.Animation;

/// <summary>Generated Composition factories and validated animation definition nodes.</summary>
public static partial class ComposeAnimation
{
    /// <summary>Represents definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>An editable Composition root compiled automatically when an animation machine is constructed.</summary>
        /// <typeparam name="TState">The external state identifier type.</typeparam>
        /// <typeparam name="TContext">The application input and callback context type.</typeparam>
        [Composable(Factory = "ComposeAnimation")]
        public partial class StateMachine<TState, TContext>
            where TState : notnull
        {
            /// <summary>Gets the initial state.</summary>
            [ComposeParameter]
            public required TState InitialState { get; init; }

            /// <summary>Gets or sets the states.</summary>
            [ComposeContent]
            public IReadOnlyList<State<TState, TContext>> States { get; set; } = [];

            internal AnimationStateMachineDefinition<TState, TContext> Compile()
            {
                ArgumentNullException.ThrowIfNull(States);
                State<TState, TContext>[] states = [.. States];
                var builder = new AnimationStateMachineBuilder<TState, TContext>();
                foreach (State<TState, TContext> state in states)
                {
                    ArgumentNullException.ThrowIfNull(state);
                    ArgumentNullException.ThrowIfNull(state.Transitions);
                    builder.AddState(state.Id, state.Timeline, state.Wrap);
                    foreach (Transition<TState, TContext> transition in state.Transitions)
                    {
                        ArgumentNullException.ThrowIfNull(transition);
                        builder.AddTransition(state.Id, transition.To, transition.Condition, transition.Trigger, transition.OnCompleted, transition.Priority);
                    }
                }

                return builder.Build(InitialState);
            }
        }

        /// <summary>Describes an animation state, its timeline and its outgoing transitions.</summary>
        /// <typeparam name="TState">The external state identifier type.</typeparam>
        /// <typeparam name="TContext">The application input and callback context type.</typeparam>
        [Composable(Factory = "ComposeAnimation")]
        public partial class State<TState, TContext>
            where TState : notnull
        {
            /// <summary>Gets the id.</summary>
            [ComposeParameter]
            public required TState Id { get; init; }

            /// <summary>Gets the timeline.</summary>
            [ComposeParameter]
            public required Timeline Timeline { get; init; }

            /// <summary>Gets the wrap.</summary>
            [ComposeParameter]
            public AnimationWrapMode Wrap { get; init; } = AnimationWrapMode.Once;

            /// <summary>Gets or sets the transitions.</summary>
            [ComposeContent]
            public IReadOnlyList<Transition<TState, TContext>> Transitions { get; set; } = [];
        }

        /// <summary>Describes an animation transition condition, trigger, completion requirement and priority.</summary>
        /// <typeparam name="TState">The external state identifier type.</typeparam>
        /// <typeparam name="TContext">The application input and callback context type.</typeparam>
        [Composable(Factory = "ComposeAnimation")]
        public partial class Transition<TState, TContext>
            where TState : notnull
        {
            /// <summary>Gets the to.</summary>
            [ComposeParameter]
            public required TState To { get; init; }

            /// <summary>Gets the condition.</summary>
            [ComposeParameter]
            public Func<TContext, AnimationStateInfo, bool>? Condition { get; init; }

            /// <summary>Gets the trigger.</summary>
            [ComposeParameter]
            public StateMachineTrigger? Trigger { get; init; }

            /// <summary>Gets a value indicating whether completion is required.</summary>
            [ComposeParameter]
            public bool OnCompleted { get; init; }

            /// <summary>Gets the priority.</summary>
            [ComposeParameter]
            public int Priority { get; init; }
        }
    }
}
