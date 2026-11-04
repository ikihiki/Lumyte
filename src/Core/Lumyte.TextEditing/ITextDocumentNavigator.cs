namespace Lumyte.TextEditing;

/// <summary>Defines itext document navigator.</summary>
public interface ITextDocumentNavigator
{
    /// <summary>Navigate.</summary>
    /// <param name = "document">The document.</param>
    /// <param name = "direction">The direction.</param>
    /// <param name = "extend">The extend.</param>
    /// <returns>The operation result.</returns>
    TextSelection Navigate(TextDocumentSnapshot document, TextNavigation direction, bool extend);
}
