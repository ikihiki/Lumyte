using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides typed control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A keyed single-selection tab group.</summary>
        [Composable]
        public partial class TabView : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="TabView"/> class.</summary>
            public TabView()
            {
                Role = UiRole.TabView;
                Focusable = false;
                Children = CreateChildren();
            }

            /// <summary>Gets the ordinary direct tab items.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <summary>Gets or sets the declared selected key; unknown keys resolve to null.</summary>
            [ComposeParameter]
            public string? SelectedKey { get; set; }

            /// <summary>Gets or sets the latest user-selection callback.</summary>
            [ComposeParameter]
            public Action<string?>? OnSelectionChanged { get; set; }

            /// <summary>Gets or sets the finite nonnegative vertical header strip width.</summary>
            [ComposeParameter]
            public float HeaderWidth { get; set; } = 120;

            /// <summary>Gets or sets the finite nonnegative height allocated to each header.</summary>
            [ComposeParameter]
            public float HeaderHeight { get; set; } = 24;

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new TabViewElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                if (!float.IsFinite(HeaderWidth) || HeaderWidth < 0 || !float.IsFinite(HeaderHeight) || HeaderHeight < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(HeaderWidth));
                }

                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (Widget child in context.GetSlotChildren(Children))
                {
                    if (child is not TabItem || string.IsNullOrEmpty(child.Key) || !keys.Add(child.Key))
                    {
                        throw new InvalidOperationException("TabView requires direct TabItems with nonempty ordinal-unique keys.");
                    }
                }
            }
        }
    }
}
