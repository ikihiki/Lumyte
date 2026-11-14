namespace Lumyte.Composition.Benchmark;

/// <summary>Minimal independent generated consumer, with no sample or UI dependency.</summary>
public static partial class Compose
{
    /// <summary>Declares generated factories and function adapters.</summary>
    public static partial class Definitions
    {
        /// <summary>Defines a function with a default construction value.</summary>
        /// <param name="value">The captured value.</param>
        /// <returns>A new node.</returns>
        [Composable]
        public static Node Value(int value = 7) => Compose.Item(value: value);

        /// <summary>Provides the function adapter policy.</summary>
        [ComposeFunctionAdapter(typeof(FunctionHost))]
        public abstract class Node
        {
            /// <summary>Gets or sets the passive identity.</summary>
            public string? Key { get; set; }
        }

        /// <summary>Hosts passive recipes and explicitly evaluates a function.</summary>
        public abstract class FunctionHost : Node, ICompositionRebuildable
        {
            /// <summary>Gets the generated reconstruction recipe.</summary>
            public Func<object>? Factory { get; private set; }

            /// <summary>Evaluates the function body explicitly.</summary>
            /// <returns>The constructed node.</returns>
            public Node Evaluate() => Build();

            /// <inheritdoc/>
            void ICompositionRebuildable.SetCompositionFactory(Func<object> factory) => Factory = factory;

            /// <summary>Builds the application result.</summary>
            /// <returns>The function result.</returns>
            protected abstract Node Build();
        }

        /// <summary>A lightweight generated content target.</summary>
        [Composable]
        public partial class Item : Node
        {
            /// <summary>Gets or sets the initial value.</summary>
            [ComposeParameter]
            public int Value { get; set; } = 7;

            /// <summary>Gets or sets the child array.</summary>
            [ComposeContent]
            public int[] Children { get; set; } = [];

            [ComposeAction]
            private static void Set(Item target, int value) => target.Value = value;
        }
    }
}
