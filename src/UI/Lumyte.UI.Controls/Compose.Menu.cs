using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides typed control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A vertical menu of directly owned items and separators.</summary>
        [Composable]
        public partial class Menu : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Menu"/> class.</summary>
            public Menu()
            {
                Role = UiRole.Menu;
                Children = CreateChildren();
            }

            /// <summary>Gets the ordinary item slot.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <summary>Gets or sets the borrowed popup host for submenu and dismissal requests.</summary>
            [ComposeParameter]
            public IMenuPopupHost? PopupHost { get; set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new MenuElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (Widget child in context.GetSlotChildren(Children))
                {
                    if (child is Lumyte.UI.Compose.Definitions.Separator)
                    {
                        if (child.Focusable)
                        {
                            throw new InvalidOperationException("A menu separator cannot be a focus target.");
                        }

                        continue;
                    }

                    if (child is not MenuItem || string.IsNullOrEmpty(child.Key) || !keys.Add(child.Key))
                    {
                        throw new InvalidOperationException("Menu requires direct MenuItems with nonempty ordinal-unique keys, or Separators.");
                    }
                }
            }
        }
    }
}
