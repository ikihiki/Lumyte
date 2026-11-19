using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Typed collection construction.</summary>
public static partial class Compose
{
    /// <summary>Passive collection declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A lazy keyed hierarchy with surface-local expansion and selection.</summary>
        /// <typeparam name="T">The node type.</typeparam>
        [Composable]
        public partial class TreeView<T> : Widget
            where T : notnull
        {
            /// <summary>Initializes a new instance of the <see cref="TreeView{T}"/> class.</summary>
            public TreeView()
            {
                Role = UiRole.Tree;
                Height = 160;
            }

            /// <summary>Gets or sets copied root nodes.</summary>
            [ComposeParameter]
            public IReadOnlyList<T> Roots { get; set; } = Array.Empty<T>();

            /// <summary>Gets or sets the required ordinal key selector.</summary>
            [ComposeParameter]
            public Func<T, string>? KeySelector { get; set; }

            /// <summary>Gets or sets children copied only when their parent is expanded.</summary>
            [ComposeParameter]
            public Func<T, IReadOnlyList<T>>? ChildrenSelector { get; set; }

            /// <summary>Gets or sets required child availability without child enumeration.</summary>
            [ComposeParameter]
            public Func<T, bool>? HasChildren { get; set; }

            /// <summary>Gets or sets an optional bounded root-to-key path resolver used only by explicit input reveal.</summary>
            [ComposeParameter]
            public Func<string, IReadOnlyList<string>>? KeyPathSelector { get; set; }

            /// <summary>Gets or sets the legacy fresh unmounted node factory, used when no Item slot is supplied.</summary>
            [ComposeParameter]
            public Func<T, Widget>? ItemTemplate { get; set; }

            /// <summary>Gets or sets expanded keys; unknown keys are retained until discovery.</summary>
            [ComposeParameter]
            public IReadOnlyList<string> ExpandedKeys { get; set; } = Array.Empty<string>();

            /// <summary>Gets or sets selection independently of visibility.</summary>
            [ComposeParameter]
            public string? SelectedKey { get; set; }

            /// <summary>Gets or sets the latest user expansion callback.</summary>
            [ComposeParameter]
            public Action<IReadOnlyList<string>>? OnExpandedChanged { get; set; }

            /// <summary>Gets or sets the latest user selection callback.</summary>
            [ComposeParameter]
            public Action<string?>? OnSelectionChanged { get; set; }

            /// <summary>Gets or sets optional model availability.</summary>
            [ComposeParameter]
            public Func<T, bool>? ItemEnabled { get; set; }

            /// <summary>Gets or sets optional visibility; invisible parents hide their descendants.</summary>
            [ComposeParameter]
            public Func<T, bool>? ItemVisible { get; set; }

            /// <summary>Gets or sets finite nonnegative indentation.</summary>
            [ComposeParameter]
            public float Indentation { get; set; } = 16;

            /// <summary>Gets or sets fixed row virtualization; null realizes every visible row.</summary>
            [ComposeParameter]
            public UiItemVirtualization? Virtualization { get; set; }

            internal Func<T, Widget>? ItemSlot { get; set; }

            internal bool HasItemTemplate => ItemSlot is not null || ItemTemplate is not null;

            internal override bool RequiresViewportPreparation => true;

            /// <inheritdoc/>
            protected override bool RebuildsChildren => true;

            internal Widget RealizeItem(T value) =>
                (ItemSlot ?? ItemTemplate)!(value) ?? throw new InvalidOperationException("A node template returned null.");

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new TreeViewElement<T>(this);

            [ComposeSlot]
            private static void Item(TreeView<T> target, CompositionTemplate<T, TreeView<T>, Widget> template) =>
                target.RegisterTemplateSlot(template, static (TreeView<T> next, CompositionTemplate<T, TreeView<T>, Widget> value) =>
                    next.ItemSlot = item => value(item, next));
        }
    }
}
