using Lumyte.Animation;
using Lumyte.Composition;
using Lumyte.Core.Time;
using Lumyte.Signals;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Defines application components using only ordinary static construction functions.</summary>
public static partial class FunctionTestUi
{
    /// <summary>Contains application-owned construction functions.</summary>
    public static partial class Definitions
    {
        /// <summary>Builds a counter with a controlled label and local persistent state.</summary>
        /// <param name="label">The current label.</param>
        /// <param name="start">The initial local value.</param>
        /// <param name="onChanged">The optional change notification.</param>
        /// <returns>The current button tree.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget Counter(string label, int start = 2, Action<int>? onChanged = null)
        {
            Signal<int> count = UseSignal(start);
            return Button(automationName: $"{label}:{count.Value}", width: 40, height: 20, onClick: () =>
            {
                count.Value++;
                onChanged?.Invoke(count.Peek());
            });
        }

        /// <summary>Builds a nested function tree.</summary>
        /// <param name="label">The child label.</param>
        /// <returns>A function child.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget Nested(string label) => FunctionTestUi.Counter(label, key: "inner");

        /// <summary>Builds directly from an external signal and optionally reads another source.</summary>
        /// <param name="select">The controlling source.</param>
        /// <param name="left">The first branch.</param>
        /// <param name="right">The second branch.</param>
        /// <returns>A dynamically selected tree.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget Branch(Signal<bool> select, Signal<string> left, Signal<string> right) =>
            Button(automationName: select.Value ? left.Value : right.Value);

        /// <summary>Builds a lazily initialized local signal.</summary>
        /// <param name="initialize">The first-build initializer.</param>
        /// <returns>The local value.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget Lazy(Func<int> initialize)
        {
            Signal<int> count = UseSignal(initialize);
            return Button(automationName: count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>Builds a generic function with a source-compatible parameter.</summary>
        /// <typeparam name="T">The displayed value type.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>The current value.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget Generic<T>(T value)
            where T : notnull => Button(automationName: value.ToString());

        /// <summary>Builds a function that can fail after allocating local state.</summary>
        /// <param name="fail">Whether to throw.</param>
        /// <returns>A counter declaration.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget Fallible(bool fail)
        {
            Signal<int> count = UseSignal(0);
            if (fail)
            {
                throw new InvalidOperationException("Function build failed.");
            }

            return Button(automationName: count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), onClick: () => count.Value++);
        }

        /// <summary>Builds a child using a source argument without reading its concrete value in the body.</summary>
        /// <returns>A button owning a source-dependent rectangle.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget ChildSource()
        {
            Signal<float?> width = UseSignal<float?>(10);
            return Button(onClick: () => width.Value = width.Peek() + 1)[Rectangle(width: width)];
        }

        /// <summary>Builds a child without a generated composition recipe.</summary>
        /// <param name="label">The current concrete label.</param>
        /// <returns>A hand-constructed button.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget Handwritten(string label) => new Lumyte.UI.Compose.Definitions.Button { AutomationName = label };

        /// <summary>Builds a leaf from a concrete animation-compatible parameter.</summary>
        /// <param name="width">The evaluated width.</param>
        /// <returns>The current rectangle.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget Sizing(float? width) => Rectangle(width: width);

        /// <summary>Counts evaluations of a source-driven function boundary.</summary>
        /// <param name="value">The displayed value and height.</param>
        /// <param name="built">Records construction.</param>
        /// <param name="onClick">The optional click notification.</param>
        /// <returns>A source-driven button.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget Observed(int value, Action built, Action? onClick = null)
        {
            built();
            return Button(width: 40, height: value, automationName: value.ToString(System.Globalization.CultureInfo.InvariantCulture), onClick: onClick);
        }

        /// <summary>Counts a parent and child that can share one invalidation batch.</summary>
        /// <param name="parent">The parent's direct read.</param>
        /// <param name="child">The child's parameter source.</param>
        /// <param name="parentBuilt">Records parent construction.</param>
        /// <param name="childBuilt">Records child construction.</param>
        /// <returns>A nested function tree.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget ObservedParent(Signal<int> parent, Signal<int> child, Action parentBuilt, Action childBuilt)
        {
            parentBuilt();
            _ = parent.Value;
            return Panel()[FunctionTestUi.Observed(child, childBuilt)];
        }

        /// <summary>Conditionally owns a child function's subscriptions.</summary>
        /// <param name="show">Whether to construct the child.</param>
        /// <param name="value">The child's parameter source.</param>
        /// <param name="built">Records child construction.</param>
        /// <returns>The optional child.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget OptionalFunction(bool show, Signal<int> value, Action built) => show ? Panel()[FunctionTestUi.Observed(value, built)] : Panel();

        /// <summary>Builds a locally validated component with a reconciliation callback.</summary>
        /// <param name="value">The width.</param>
        /// <param name="reconciled">Requests another declaration during preparation.</param>
        /// <returns>The custom component.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget ReentrantBody(int value, Action reconciled) => new ReentrantPanel(reconciled) { Width = value };

        /// <summary>Creates an animation producer inside a partially rebuilt function.</summary>
        /// <param name="target">The desired width.</param>
        /// <param name="version">An independent rebuild trigger.</param>
        /// <returns>The animated rectangle.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget AnimatedFunction(Signal<float?> target, Signal<int> version)
        {
            _ = version.Value;
            return Rectangle(width: Animate(target, Duration.FromSeconds(1), UiAnimationInterpolators.Nullable(AnimationInterpolators.Float), AnimationEasing.Linear));
        }

        /// <summary>Builds a shared element with a source-driven identity.</summary>
        /// <param name="sharedKey">The shared visual key.</param>
        /// <returns>The shared rectangle.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget SharedChoice(string sharedKey) => Rectangle(sharedElementKey: new UiSharedElementKey(sharedKey));

        /// <summary>Adds a conditional local call before an existing state call site.</summary>
        /// <param name="extra">Whether to initialize the optional call site.</param>
        /// <returns>The counter at the stable call site.</returns>
        [Composable(Factory = "FunctionTestUi")]
        public static Widget Conditional(bool extra)
        {
            if (extra)
            {
                _ = UseSignal(100);
            }

            Signal<int> count = UseSignal(0);
            return Button(automationName: count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), onClick: () => count.Value++);
        }
    }

    private sealed class ReentrantPanel(Action reconciled) : Lumyte.UI.Compose.Definitions.Panel
    {
        internal Action Reconciled { get; } = reconciled;

        protected override UiElement CreateElement() => new ReentrantElement(this);
    }

    private sealed class ReentrantElement(ReentrantPanel widget) : UiElement<ReentrantPanel>(widget)
    {
        protected override Action? PrepareUpdate(ReentrantPanel next, UiElementUpdateContext context)
        {
            if (IsMounted)
            {
                next.Reconciled();
            }

            return null;
        }
    }
}
