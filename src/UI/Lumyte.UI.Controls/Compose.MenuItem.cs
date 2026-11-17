using System.Numerics;
using Lumyte.Composition;
using Lumyte.Mathematics.Colors;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides typed control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A keyed menu command, checkable command, or submenu anchor.</summary>
        [Composable]
        public partial class MenuItem : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="MenuItem"/> class.</summary>
            public MenuItem()
            {
                Role = UiRole.MenuItem;
                Focusable = true;
                Children = CreateChildren(1);
            }

            /// <summary>Gets the optional visual label, not a nested button.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <summary>Gets or sets the latest command callback.</summary>
            [ComposeParameter]
            public Action? OnInvoke { get; set; }

            /// <summary>Gets or sets a factory returning a fresh independent Menu once per open.</summary>
            [ComposeParameter]
            public Func<Widget>? CreateSubmenu { get; set; }

            /// <summary>Gets or sets a value indicating whether ordinary activation toggles a bool.</summary>
            [ComposeParameter]
            public bool IsCheckable { get; set; }

            /// <summary>Gets or sets a value indicating whether the item is initially checked, adopted silently.</summary>
            [ComposeParameter]
            public bool IsChecked { get; set; }

            /// <summary>Gets or sets the latest accepted user-check callback.</summary>
            [ComposeParameter]
            public Action<bool>? OnChanged { get; set; }

            /// <summary>Gets or sets the explicit check and submenu mark color.</summary>
            [ComposeParameter]
            public SRgba<float> Foreground { get; set => field = ControlStyle.Validate(value); } = new(1, 1, 1, 1);

            /// <summary>Gets or sets positive finite popup dimensions.</summary>
            [ComposeParameter]
            public Vector2 PopupSize { get; set; } = new(240, 160);

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new MenuItemElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                if (string.IsNullOrEmpty(Key) || context.GetParent(this) is not Menu parent
                    || !context.GetSlotChildren(parent.Children).Contains(this))
                {
                    throw new InvalidOperationException("MenuItem requires a keyed ordinary direct Menu parent.");
                }

                if (CreateSubmenu is not null && (OnInvoke is not null || IsCheckable || OnChanged is not null))
                {
                    throw new InvalidOperationException("A submenu cannot also be a command or checkable item.");
                }

                if (CreateSubmenu is not null && parent.PopupHost is null)
                {
                    throw new InvalidOperationException("A submenu requires its Menu's borrowed popup host.");
                }

                PopupReservation.ValidateSize(PopupSize);
            }
        }
    }
}
