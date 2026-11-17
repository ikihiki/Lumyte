using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides typed control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A retained collapsible panel with explicit header and content.</summary>
        [Composable]
        public partial class Expander : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Expander"/> class.</summary>
            public Expander()
            {
                Role = UiRole.Expander;
                Focusable = true;
                Children = CreateChildren(0);
                HeaderChildren = CreateChildren(1);
                ContentChildren = CreateChildren(1);
            }

            /// <summary>Gets the empty ordinary content slot; use named slots.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <summary>Gets the optional header slot.</summary>
            public WidgetChildren HeaderChildren { get; }

            /// <summary>Gets the optional retained content slot.</summary>
            public WidgetChildren ContentChildren { get; }

            /// <summary>Gets or sets a value indicating whether content is expanded, adopted silently.</summary>
            [ComposeParameter]
            public bool IsExpanded { get; set; }

            /// <summary>Gets or sets the latest user-change callback.</summary>
            [ComposeParameter]
            public Action<bool>? OnChanged { get; set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ExpanderElement(this);

            [ComposeSlot]
            private static void Header(Expander target, Widget[] children) => target.HeaderChildren.Initialize(children);

            [ComposeSlot]
            private static void Content(Expander target, Widget[] children) => target.ContentChildren.Initialize(children);
        }
    }
}
