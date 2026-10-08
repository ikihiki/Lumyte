namespace Lumyte.Composition.Sample;

/// <summary>Provides generated factories for the sample definitions.</summary>
public static partial class Compose
{
    /// <summary>Contains component types, separate from same-named factory properties.</summary>
    public static partial class Definitions
    {
        /// <summary>A widget owning its own property storage independently of Composition.</summary>
        public abstract class Widget
        {
            /// <summary>Gets widget-owned attached values.</summary>
            public IDictionary<string, object?> AttachedValues { get; } = new Dictionary<string, object?>();

            /// <summary>Gets or sets widget opacity.</summary>
            public float Opacity { get; set; } = 1;
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
