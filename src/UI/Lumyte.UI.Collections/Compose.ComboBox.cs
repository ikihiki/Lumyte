using System.Numerics;
using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Typed collection construction.</summary>
public static partial class Compose
{
    /// <summary>Passive collection declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A non-editable single-choice popup selector.</summary>
        /// <typeparam name="T">The application item type.</typeparam>
        [Composable]
        public partial class ComboBox<T> : ListBox<T>
            where T : notnull
        {
            /// <summary>Initializes a new instance of the <see cref="ComboBox{T}"/> class.</summary>
            public ComboBox()
            {
                Role = UiRole.ComboBox;
                Focusable = true;
                Height = 32;
            }

            /// <summary>Gets or sets the borrowed popup host.</summary>
            [ComposeParameter]
            public ICollectionPopupHost? PopupHost { get; set; }

            /// <summary>Gets or sets legacy fresh closed-display content, used when no SelectedItem slot is supplied.</summary>
            [ComposeParameter]
            public Func<T, Widget>? SelectedItemTemplate { get; set; }

            /// <summary>Gets or sets the legacy fresh empty-display factory, used when no Empty slot is supplied.</summary>
            [ComposeParameter]
            public Func<Widget>? EmptyTemplate { get; set; }

            /// <summary>Gets or sets the explicit positive popup viewport.</summary>
            [ComposeParameter]
            public Vector2 PopupSize { get; set; } = new(200, 160);

            internal Func<T, Widget>? SelectedItemSlot { get; set; }

            internal Func<Widget>? EmptySlot { get; set; }

            internal bool HasEmptyTemplate => EmptySlot is not null || EmptyTemplate is not null;

            internal bool HasSelectedItemTemplate => SelectedItemSlot is not null || SelectedItemTemplate is not null;

            internal Widget RealizeEmpty() =>
                (EmptySlot ?? EmptyTemplate)!() ?? throw new InvalidOperationException("An empty template returned null.");

            internal Widget RealizeSelectedItem(T value) =>
                (SelectedItemSlot ?? SelectedItemTemplate)!(value) ?? throw new InvalidOperationException("A selected item template returned null.");

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ComboBoxElement<T>(this);

            [ComposeSlot]
            private static void Item(ComboBox<T> target, CompositionTemplate<T, ComboBox<T>, Widget> template) =>
                target.RegisterTemplateSlot(template, static (ComboBox<T> next, CompositionTemplate<T, ComboBox<T>, Widget> value) =>
                    next.ItemSlot = item => value(item, next));

            [ComposeSlot]
            private static void SelectedItem(ComboBox<T> target, CompositionTemplate<T, ComboBox<T>, Widget> template) =>
                target.RegisterTemplateSlot(template, static (ComboBox<T> next, CompositionTemplate<T, ComboBox<T>, Widget> value) =>
                    next.SelectedItemSlot = item => value(item, next));

            [ComposeSlot]
            private static void Empty(ComboBox<T> target, CompositionTemplate<ComboBox<T>, Widget> template) =>
                target.RegisterTemplateSlot(template, static (ComboBox<T> next, CompositionTemplate<ComboBox<T>, Widget> value) =>
                    next.EmptySlot = () => value(next));
        }
    }
}
