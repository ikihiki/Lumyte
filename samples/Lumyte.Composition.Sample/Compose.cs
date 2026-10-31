namespace Lumyte.Composition.Sample;

/// <summary>Provides generated factories for the sample definitions.</summary>
public static partial class Compose
{
    /// <summary>Contains component types, separate from same-named factory properties.</summary>
    public static partial class Definitions
    {
        /// <summary>Constructs text from ordinary function parameters.</summary>
        /// <param name="label">The text, including an explicit null.</param>
        /// <param name="onBuild">The notification explicitly invoked by this definition.</param>
        /// <returns>A text node.</returns>
        [Composable]
        public static Widget Greeting(string? label = "hello", Action? onBuild = null)
        {
            onBuild?.Invoke();
            return Compose.Text(content: label);
        }

        /// <summary>Constructs text from a closed generic value.</summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="value">The value to display.</param>
        /// <returns>A text node.</returns>
        [Composable]
        public static Widget Typed<T>(T value)
            where T : notnull => Compose.Text(content: value.ToString());

        /// <summary>A widget owning its own property storage independently of Composition.</summary>
        [ComposeFunctionAdapter(typeof(FunctionWidget))]
        public abstract class Widget
        {
            /// <summary>Gets or sets the host-defined declaration identity.</summary>
            public string? Key { get; set; }

            /// <summary>Gets widget-owned attached values.</summary>
            public IDictionary<string, object?> AttachedValues { get; } = new Dictionary<string, object?>();

            /// <summary>Gets or sets widget opacity.</summary>
            public float Opacity { get; set; } = 1;
        }

        /// <summary>A sample host that explicitly evaluates generated function declarations.</summary>
        public abstract class FunctionWidget : Widget, ICompositionRebuildable
        {
            /// <summary>Initializes a new instance of the <see cref="FunctionWidget"/> class.</summary>
            protected FunctionWidget()
            {
            }

            /// <summary>Gets the passive recipe registered by the generated factory.</summary>
            public Func<object>? Factory { get; private set; }

            /// <summary>Evaluates a function when the host chooses to do so.</summary>
            /// <returns>The tree returned by the definition.</returns>
            public Widget Evaluate() => Build();

            /// <inheritdoc/>
            void ICompositionRebuildable.SetCompositionFactory(Func<object> factory) => Factory = factory;

            /// <summary>Invokes the application-defined construction function.</summary>
            /// <returns>The application tree.</returns>
            protected abstract Widget Build();
        }

        /// <summary>A container declaring operations on widgets.</summary>
        [Composable]
        public partial class Grid : Widget
        {
            /// <summary>Gets or sets the ordered child widgets.</summary>
            [ComposeContent]
            public IReadOnlyList<Widget> Children { get; set; } = [];

            [ComposeAction]
            private static void Column(Widget target, int value)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                target.AttachedValues["Grid.Column"] = value;
            }
        }

        /// <summary>A button with ordinary content and a named background slot.</summary>
        [Composable]
        public partial class Button : Widget
        {
            /// <summary>Gets or sets ordinary button content.</summary>
            [ComposeContent]
            public IReadOnlyList<Widget> Children { get; set; } = [];

            /// <summary>Gets the background children owned by this button.</summary>
            public IReadOnlyList<Widget> BackgroundChildren { get; private set; } = [];

            [ComposeSlot]
            private static void Background(Button target, IReadOnlyList<Widget> children) => target.BackgroundChildren = children;
        }

        /// <summary>An image widget with a required path.</summary>
        [Composable]
        public partial class Image : Widget
        {
            /// <summary>Gets or initializes the image path.</summary>
            [ComposeParameter]
            public required string Source { get; init; }
        }

        /// <summary>A generic list widget with typed items and operations.</summary>
        /// <typeparam name="T">The item type.</typeparam>
        [Composable]
        public partial class ListView<T> : Widget
            where T : notnull
        {
            /// <summary>Gets or sets the selected item.</summary>
            [ComposeParameter]
            public T Selected { get; set; } = default!;

            /// <summary>Gets or sets the list items.</summary>
            [ComposeContent]
            public IReadOnlyList<T> Items { get; set; } = [];

            [ComposeAction]
            private static void Select(ListView<T> target, T value) => target.Selected = value;
        }

        /// <summary>A text widget receiving initial settings and style operations.</summary>
        [Composable]
        public partial class Text : Widget
        {
            /// <summary>Gets or initializes the text content.</summary>
            [ComposeParameter]
            public string? Content { get; init; } = "untitled";

            [ComposeAction]
            private static void Fade(Widget target, float opacity) => target.Opacity = opacity;

            [ComposeAction]
            private static void Reset(Widget target) => target.Opacity = 1;

            [ComposeAction]
            private static void Tag(Widget target, string key, object? value) => target.AttachedValues[key] = value;
        }
    }
}
