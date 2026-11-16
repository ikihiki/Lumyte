using Lumyte.Composition;

namespace Lumyte.UI.WebView;

/// <summary>Provides passive browser placement factories.</summary>
public static partial class Compose
{
    /// <summary>Contains browser declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>Places a borrowed window-owned browser session.</summary>
        [Composable]
        public partial class WebView : Lumyte.UI.Compose.Definitions.Widget
        {
            /// <summary>Initializes a new instance of the <see cref="WebView"/> class.</summary>
            public WebView()
            {
                Role = UiRole.Group;
                Focusable = true;
                ClipToBounds = true;
            }

            /// <summary>Gets or sets the required borrowed session; never initiates navigation.</summary>
            [ComposeParameter]
            public UiWebViewSession? Session { get; set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new WebViewElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context) => ArgumentNullException.ThrowIfNull(Session);
        }
    }
}
