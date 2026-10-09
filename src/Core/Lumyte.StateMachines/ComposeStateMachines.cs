using Lumyte.Composition;

namespace Lumyte.StateMachines;

/// <summary>Generated Composition construction nodes for typed state machine definitions and instances.</summary>
public static partial class ComposeStateMachines
{
    /// <summary>Represents definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>Represents machine.</summary>
        /// <typeparam name="TContext">The application input and callback context type.</typeparam>
        /// <typeparam name="TTrigger">The trigger value type.</typeparam>
        [Composable(Factory = "ComposeStateMachines")]
        public partial class Machine<TContext, TTrigger>
        {
            /// <summary>Gets the initial state.</summary>
            [ComposeParameter]
            public required State<TContext> InitialState { get; init; }

            /// <summary>Gets or sets the transitions.</summary>
            [ComposeContent]
            public IReadOnlyList<Transition<TContext, TTrigger>> Transitions { get; set; } = [];

            /// <summary>Validates the definition, creates an independent execution instance and runs its initial entry actions.</summary>
            /// <param name="context">The context.</param>
            /// <returns>The computed result.</returns>
            public StateMachineInstance<TContext, TTrigger> Build(TContext context)
            {
                if (context is null)
                {
                    throw new ArgumentNullException(nameof(context));
                }

                return new StateMachineInstance<TContext, TTrigger>(BuildDefinition(), context);
            }

            /// <summary>Validates, snapshots and freezes a reusable definition without executing entry actions.</summary>
            /// <returns>The computed result.</returns>
            public StateMachine<TContext, TTrigger> BuildDefinition()
            {
                ArgumentNullException.ThrowIfNull(InitialState);
                ArgumentNullException.ThrowIfNull(Transitions);
                Transition<TContext, TTrigger>[] transitions = [.. Transitions];
                if (transitions.Any(item => item is null))
                {
                    throw new ArgumentException("A definition cannot contain a null transition.");
                }

                var builder = new StateMachineBuilder<TContext, TTrigger>(InitialState);
                foreach (Transition<TContext, TTrigger> transition in transitions)
                {
                    builder.AddTransition(transition);
                }

                return builder.BuildDefinition();
            }
        }
    }
}
