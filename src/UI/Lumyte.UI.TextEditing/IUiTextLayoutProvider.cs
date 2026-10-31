using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing;

/// <summary>Defines iui text layout provider.</summary>
public interface IUiTextLayoutProvider
{
    /// <summary>Create Layout.</summary>
    /// <param name = "document">The document.</param>
    /// <param name = "maximumWidth">The maximum width.</param>
    /// <returns>The operation result.</returns>
    UiTextLayout CreateLayout(TextDocumentSnapshot document, float maximumWidth);
}
