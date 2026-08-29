using System.Drawing;

using Lumyte.Input;

namespace Lumyte.Platform.Windows.Tests;

internal sealed class FakeTextInputClient : ITextInputClient
{
    public string Text { get; private set; } = string.Empty;

    public TextRange Selection { get; private set; }

    public RectangleF? CaretBounds { get; set; }

    public TextCandidatePresentation CandidatePresentation { get; set; }

    public TextRange Composition { get; private set; }

    public TextRange? CompositionTarget { get; private set; }

    public TextCandidateList? Candidates { get; private set; }

    public void Select(TextRange selection) => Selection = selection;

    public void Replace(TextRange range, string text)
    {
        Text = string.Concat(Text.AsSpan(0, range.Start), text, Text.AsSpan(range.End));
        Selection = new(range.Start + text.Length, 0);
    }

    public void SetComposition(TextRange composition, TextRange? target)
    {
        Composition = composition;
        CompositionTarget = target;
    }

    public void SetCandidates(TextCandidateList? candidates) => Candidates = candidates;
}
