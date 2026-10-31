using Lumyte.Composition;
using Lumyte.Signals;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Functions.Sample;

/// <summary>Contains application factories generated from static functions.</summary>
public static partial class CounterUi
{
    /// <summary>Contains only ordinary construction functions, without custom widget subclasses.</summary>
    public static partial class Definitions
    {
        /// <summary>Reads inherited context and retains a custom hook value before returning another function.</summary>
        /// <param name="context">The label context.</param>
        /// <param name="initialize">The first-build suffix factory.</param>
        /// <returns>A nested counter declaration.</returns>
        [Composable(Factory = "CounterUi")]
        public static Widget ContextCounter(UiContextKey<string> context, Func<string> initialize)
        {
            string suffix = Use(initialize);
            return CounterUi.Counter(UseContext(context) + suffix, key: "inner");
        }

        /// <summary>Builds a counter from current parameters and persistent local state.</summary>
        /// <param name="label">The current label.</param>
        /// <param name="start">The initial counter value.</param>
        /// <param name="onChanged">The optional change callback.</param>
        /// <returns>The button representing this function's current UI.</returns>
        [Composable(Factory = "CounterUi")]
        public static Widget Counter(string label, int start = 0, Action<int>? onChanged = null)
        {
            Signal<int> count = UseSignal(start);
            return Button(width: 120, height: 40, automationName: $"{label}: {count.Value}", onClick: () =>
            {
                count.Value++;
                onChanged?.Invoke(count.Peek());
            });
        }
    }
}
