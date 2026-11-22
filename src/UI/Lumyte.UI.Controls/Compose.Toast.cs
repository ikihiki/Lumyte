using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides typed control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>Ordinary content; presentation and lifetime belong to its host.</summary>
        [Composable]
        public partial class Toast : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Toast"/> class.</summary>
            public Toast()
            {
                Role = UiRole.Group;
                Children = CreateChildren(0);
                HeaderChildren = CreateChildren(1);
                ContentChildren = CreateChildren(1);
                ActionsChildren = CreateChildren(1);
                DismissChildren = CreateChildren(1);
            }

            /// <summary>Gets the empty ordinary slot; use named slots.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <summary>Gets the optional heading.</summary>
            public WidgetChildren HeaderChildren { get; }

            /// <summary>Gets the optional body.</summary>
            public WidgetChildren ContentChildren { get; }

            /// <summary>Gets the optional action root, which may contain ordinary buttons.</summary>
            public WidgetChildren ActionsChildren { get; }

            /// <summary>Gets the optional visual content for the ordinary dismissal button.</summary>
            public WidgetChildren DismissChildren { get; }

            /// <summary>Gets or sets the latest dismissal request callback; the model is not changed automatically.</summary>
            [ComposeParameter]
            public Action? OnDismissRequested { get; set; }

            /// <summary>Gets or sets the explicit dismissal button name.</summary>
            [ComposeParameter]
            public string DismissName { get; set; } = "Dismiss notification";

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ToastElement(this);

            [ComposeSlot]
            private static void Dismiss(Toast target, Widget[] children) => target.DismissChildren.Initialize(children);

            [ComposeSlot]
            private static void Header(Toast target, Widget[] children) => target.HeaderChildren.Initialize(children);

            [ComposeSlot]
            private static void Content(Toast target, Widget[] children) => target.ContentChildren.Initialize(children);

            [ComposeSlot]
            private static void Actions(Toast target, Widget[] children) => target.ActionsChildren.Initialize(children);
        }
    }
}
