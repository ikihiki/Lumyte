namespace Lumyte.Platform.Abstractions;

/// <summary>Defines ITextInputSession.</summary>
public interface ITextInputSession : IDisposable
{
    /// <summary>Occurs when CompositionChanged is reported.</summary>
    event Action<bool>? CompositionChanged;

    /// <summary>Gets a value indicating whether IsComposing is enabled.</summary>
    bool IsComposing { get; }

    /// <summary>Gets a value indicating whether UsesSystemCandidateUi is enabled.</summary>
    bool UsesSystemCandidateUi { get; }

    /// <summary>Performs Focus.</summary>
    void Focus();

    /// <summary>Performs Blur.</summary>
    void Blur();

    /// <summary>Performs NotifyTextChanged.</summary>
    /// <param name="start">The start value.</param>
    /// <param name="oldLength">The oldLength value.</param>
    /// <param name="newLength">The newLength value.</param>
    void NotifyTextChanged(int start, int oldLength, int newLength);

    /// <summary>Performs NotifySelectionChanged.</summary>
    void NotifySelectionChanged();

    /// <summary>Performs NotifyLayoutChanged.</summary>
    void NotifyLayoutChanged();
}
