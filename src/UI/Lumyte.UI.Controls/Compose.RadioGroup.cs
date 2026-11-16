using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Provides generated typed control factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive control declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A passive RadioGroup declaration.</summary>
        [Composable]
        public partial class RadioGroup : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="RadioGroup"/> class.</summary>
            public RadioGroup()
            {
                Role = UiRole.RadioGroup;
                Focusable = false;
                Children = CreateChildren();
                BackgroundChildren = CreateChildren();
            }

            /// <summary>Gets ordinary content, initialized once.</summary>
            [ComposeContent]
            public WidgetChildren Children { get; }

            /// <summary>Gets the optional background slot.</summary>
            public WidgetChildren BackgroundChildren { get; }

            /// <summary>Gets or sets the declared ordinal selected key; missing keys resolve to null.</summary>
            [ComposeParameter]
            public string? SelectedKey { get; set; }

            /// <summary>Gets or sets the latest user-change callback.</summary>
            [ComposeParameter]
            public Action<string?>? OnChanged { get; set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new RadioGroupElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (Widget child in context.GetChildren(this))
                {
                    if (child is RadioButton && (string.IsNullOrEmpty(child.Key) || !keys.Add(child.Key)))
                    {
                        throw new InvalidOperationException("Radio option keys must be nonempty and ordinal unique.");
                    }
                }
            }

            [ComposeSlot]
            private static void Background(RadioGroup target, Widget[] children) => target.BackgroundChildren.Initialize(children);
        }
    }
}
