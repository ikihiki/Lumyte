using Lumyte.Composition;
using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing;

/// <summary>Defines compose.</summary>
public static partial class Compose
{
    /// <summary>Defines definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>Displays a copied IME candidate list without acquiring keyboard focus or exposing candidate text as ordinary UI metadata.</summary>
        [Composable]
        public partial class ImeCandidateView : Lumyte.UI.Compose.Definitions.Widget
        {
            private ImeCandidateSnapshot? _candidates;

            /// <summary>Initializes a new instance of the <see cref = "ImeCandidateView"/> class.</summary>
            public ImeCandidateView()
            {
                Focusable = false;
                Role = UiRole.ImeCandidates;
                ClipToBounds = true;
            }

            /// <summary>Gets or sets the candidates.</summary>
            [ComposeParameter]
            public ImeCandidateSnapshot? Candidates { get => _candidates; set => _candidates = value is null ? null : ImeCandidateValidation.Copy(value); }

            /// <summary>Gets or sets the layout provider.</summary>
            [ComposeParameter]
            public IUiTextLayoutProvider? LayoutProvider { get; set; }

            /// <summary>Gets or sets the style.</summary>
            [ComposeParameter]
            public ImeCandidateStyle Style { get; set; } = ImeCandidateStyle.Default;

            /// <summary>Gets or sets the on action.</summary>
            [ComposeParameter]
            public Action<ImeCandidateAction>? OnAction { get; set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ImeCandidateViewElement(this);
        }
    }
}
