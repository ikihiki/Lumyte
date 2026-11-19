using System.Numerics;
using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Windowing;

/// <summary>Creates passive windowing declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains windowing component definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>A zero-sized declaration whose content is owned by an independent dialog surface.</summary>
        [Composable]
        public partial class Dialog : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Dialog"/> class.</summary>
            public Dialog()
            {
                Content = CreatePortalChildren();
            }

            /// <summary>Gets or sets the explicit semantic title.</summary>
            [ComposeParameter]
            public string Title { get; set; } = "Dialog";

            /// <summary>Gets or sets a value indicating whether open is requested; unavailable owners defer admission, and retained true intent reopens once after the old drain and owner return.</summary>
            [ComposeParameter]
            public bool IsOpen { get; set; }

            /// <summary>Gets or sets a value indicating whether this dialog shields its logical keyboard scope.</summary>
            [ComposeParameter]
            public bool IsModal { get; set; } = true;

            /// <summary>Gets or sets the presentation policy.</summary>
            [ComposeParameter]
            public DialogPresentation Presentation { get; set; }

            /// <summary>Gets or sets a value indicating whether Escape requests closure; modal Escape is always consumed.</summary>
            [ComposeParameter]
            public bool CloseOnEscape { get; set; } = true;

            /// <summary>Gets or sets a value indicating whether an outside modal press requests closure.</summary>
            [ComposeParameter]
            public bool CloseOnOutsidePress { get; set; }

            /// <summary>Gets the finite positive opening size, retained for the entire session.</summary>
            [ComposeParameter]
            public Vector2 InitialClientSize { get; init; } = new(320, 200);

            /// <summary>Gets an optional content key to focus initially; otherwise the first eligible target is used.</summary>
            [ComposeParameter]
            public string? InitialFocus { get; init; }

            /// <summary>Gets or sets the latest controlled close callback.</summary>
            [ComposeParameter]
            public Action<UiDialogCloseRequest>? OnCloseRequested { get; set; }

            /// <summary>Gets or sets the latest actual-open callback.</summary>
            [ComposeParameter]
            public Action<UiDialogOpened>? OnOpened { get; set; }

            /// <summary>Gets or sets the latest after-drain callback.</summary>
            [ComposeParameter]
            public Action<UiDialogClosed>? OnClosed { get; set; }

            /// <summary>Gets single-assignment portal content; it is not mounted on the declaring surface.</summary>
            [ComposeContent]
            public WidgetChildren Content { get; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new DialogElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(Key);
                ArgumentException.ThrowIfNullOrWhiteSpace(Title);
                if (!Enum.IsDefined(Presentation) || !float.IsFinite(InitialClientSize.X) || !float.IsFinite(InitialClientSize.Y) || InitialClientSize.X <= 0 || InitialClientSize.Y <= 0)
                {
                    throw new ArgumentException("Dialog policy and opening size must be valid.");
                }
            }
        }
    }
}
