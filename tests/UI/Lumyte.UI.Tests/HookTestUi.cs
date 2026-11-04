using Lumyte.Composition;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Defines function components consuming common hooks.</summary>
public static partial class HookTestUi
{
    /// <summary>Contains component definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>Reads a provided value.</summary>
        /// <param name="context">The context identity.</param>
        /// <returns>The borrowed value as a button label.</returns>
        [Composable(Factory = "HookTestUi")]
        public static Widget Reader(UiContextKey<string> context) => Button(automationName: UseContext(context));

        /// <summary>Creates child function boundaries and a nearer provider.</summary>
        /// <param name="context">The context identity.</param>
        /// <returns>Three independently evaluated children.</returns>
        [Composable(Factory = "HookTestUi")]
        public static Widget Parent(UiContextKey<string> context) => Panel()[
            HookTestUi.Reader(context),
            Provide(context, "inner", HookTestUi.Reader(context)),
            HookTestUi.Reader(context)];

        /// <summary>Retains arbitrary nullable state through the shared Use path.</summary>
        /// <param name="initialize">The initial state factory.</param>
        /// <param name="observe">Receives the retained value.</param>
        /// <returns>A reactive counter sharing the common hook frame.</returns>
        [Composable(Factory = "HookTestUi")]
        public static Widget State(Func<object?> initialize, Action<object?> observe)
        {
            object? value = Use(initialize);
            Lumyte.Signals.Signal<int> count = UseSignal(0);
            observe(value);
            return Button(automationName: count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), onClick: () => count.Value++);
        }

        /// <summary>Checks nested hosts restore the caller's execution frame.</summary>
        /// <param name="context">The context identity.</param>
        /// <param name="run">Runs another host, optionally failing.</param>
        /// <returns>The values read before and after the nested host.</returns>
        [Composable(Factory = "HookTestUi")]
        public static Widget Reentrant(UiContextKey<string> context, Action run)
        {
            string before = UseContext(context);
            run();
            return Button(automationName: before + ":" + UseContext(context));
        }

        /// <summary>Calls a definition directly as a synchronous helper.</summary>
        /// <param name="context">The context identity.</param>
        /// <returns>The helper's tree using the caller frame.</returns>
        [Composable(Factory = "HookTestUi")]
        public static Widget Inline(UiContextKey<string> context) => Reader(context);
    }
}
