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
        public partial class DialogContent : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="DialogContent"/> class.</summary>
            public DialogContent()
            {
                Role = UiRole.Group;
                Children = CreateChildren(0);
                HeaderChildren = CreateChildren(1);
                ContentChildren = CreateChildren(1);
                ActionsChildren = CreateChildren(1);
                CloseChildren = CreateChildren(1);
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

            /// <summary>Gets optional visual content for a host close button; an empty slot omits the button.</summary>
            public WidgetChildren CloseChildren { get; }

            /// <summary>Gets or sets the explicit host close button name.</summary>
            [ComposeParameter]
            public string CloseName { get; set; } = "Close dialog";

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new DialogContentElement(this);

            [ComposeSlot]
            private static void Close(DialogContent target, Widget[] children) => target.CloseChildren.Initialize(children);

            [ComposeSlot]
            private static void Header(DialogContent target, Widget[] children) => target.HeaderChildren.Initialize(children);

            [ComposeSlot]
            private static void Content(DialogContent target, Widget[] children) => target.ContentChildren.Initialize(children);

            [ComposeSlot]
            private static void Actions(DialogContent target, Widget[] children) => target.ActionsChildren.Initialize(children);
        }
    }
}
