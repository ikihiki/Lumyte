using Lumyte.UI.TextEditing;

namespace Lumyte.UI.Windowing.TextInput;

/// <summary>Defines ui ime candidate presenter.</summary>
public sealed class UiImeCandidatePresenter
{
    /// <summary>Initializes a new instance of the <see cref = "UiImeCandidatePresenter"/> class.</summary>
    /// <param name = "windows">The windows.</param>
    /// <param name = "provider">The provider.</param>
    /// <param name = "style">The style.</param>
    public UiImeCandidatePresenter(IUiWindowManager windows, IUiTextLayoutProvider provider, ImeCandidateStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(provider);
        Windows = windows;
        Provider = provider;
        Style = style ?? ImeCandidateStyle.Default;
    }

    internal IUiWindowManager Windows { get; }

    internal IUiTextLayoutProvider Provider { get; }

    internal ImeCandidateStyle Style { get; }
}
