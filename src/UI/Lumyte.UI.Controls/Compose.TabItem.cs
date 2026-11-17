using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides typed control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A retained keyed tab panel with explicit header and content.</summary>
        [Composable]
        public partial class TabItem : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="TabItem"/> class.</summary>
            public TabItem()
            {
                Role = UiRole.TabItem;
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

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new TabItemElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                if (string.IsNullOrEmpty(Key) || context.GetParent(this) is not TabView parent
                    || !context.GetSlotChildren(parent.Children).Contains(this))
                {
                    throw new InvalidOperationException("TabItem requires a nonempty inherited Key and an ordinary direct TabView parent.");
                }
            }

            [ComposeSlot]
            private static void Header(TabItem target, Widget[] children) => target.HeaderChildren.Initialize(children);

            [ComposeSlot]
            private static void Content(TabItem target, Widget[] children) => target.ContentChildren.Initialize(children);
        }
    }
}
