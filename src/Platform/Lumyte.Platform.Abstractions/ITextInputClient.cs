namespace Lumyte.Platform.Abstractions;

/// <summary>Defines ITextInputClient.</summary>
public interface ITextInputClient
{
    /// <summary>Gets Text.</summary>
    string Text { get; }

    /// <summary>Gets Selection.</summary>
    TextSelection Selection { get; }

    /// <summary>Gets a value indicating whether IsReadOnly is enabled.</summary>
    bool IsReadOnly { get; }

    /// <summary>Performs Replace.</summary>
    /// <param name="start">The start value.</param>
    /// <param name="length">The length value.</param>
    /// <param name="text">The text value.</param>
    void Replace(int start, int length, string text);

    /// <summary>Performs SetSelection.</summary>
    /// <param name="selection">The selection value.</param>
    void SetSelection(TextSelection selection);

    /// <summary>Performs GetBounds.</summary>
    /// <param name="start">The start value.</param>
    /// <param name="length">The length value.</param>
    /// <returns>The operation result.</returns>
    TextInputBounds GetBounds(int start, int length);
}
