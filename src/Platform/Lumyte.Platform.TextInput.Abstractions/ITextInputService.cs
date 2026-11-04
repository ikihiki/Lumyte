using Lumyte.TextEditing;

namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines itext input service.</summary>
public interface ITextInputService : IDisposable
{
    /// <summary>Gets the features.</summary>
    TextInputFeatures Features { get; }

    /// <summary>Begin.</summary>
    /// <param name = "target">The target.</param>
    /// <param name = "document">The document.</param>
    /// <param name = "navigator">The navigator.</param>
    /// <returns>The operation result.</returns>
    ITextInputSession Begin(TextInputTarget target, TextDocument document, ITextDocumentNavigator navigator);

    /// <summary>Advance.</summary>
    void Advance();

    /// <summary>Drain Keys.</summary>
    /// <returns>The operation result.</returns>
    IReadOnlyList<TextInputKey> DrainKeys();
}
