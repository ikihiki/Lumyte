using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing;

/// <summary>Provides CPU-only visual navigation without retaining a widget or surface.</summary>
public sealed class UiTextDocumentNavigator : ITextDocumentNavigator
{
    private IUiTextLayoutProvider _provider;

    /// <summary>Initializes a new instance of the <see cref = "UiTextDocumentNavigator"/> class.</summary>
    /// <param name = "provider">The borrowed CPU layout provider.</param>
    public UiTextDocumentNavigator(IUiTextLayoutProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <inheritdoc/>
    public TextSelection Navigate(TextDocumentSnapshot document, TextNavigation direction, bool extend)
    {
        UiTextLayout layout = _provider.CreateLayout(document, float.PositiveInfinity);
        var current = new UiTextCaret(document.Selection.Active, document.Selection.ActiveAffinity);
        UiTextCaret next = direction switch
        {
            TextNavigation.Previous => layout.MoveCaret(current, false),
            TextNavigation.Next => layout.MoveCaret(current, true),
            TextNavigation.Start => layout.CaretStops.FirstOrDefault(),
            TextNavigation.End => layout.CaretStops.LastOrDefault(),
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
        return new(extend ? document.Selection.Anchor : next.Position, next.Position, extend ? document.Selection.AnchorAffinity : next.Affinity, next.Affinity);
    }

    internal void UpdateProvider(IUiTextLayoutProvider provider) => _provider = provider;
}
