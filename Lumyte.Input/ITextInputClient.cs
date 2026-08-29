using System.Drawing;

namespace Lumyte.Input;

public interface ITextInputClient
{
    string Text { get; }

    TextRange Selection { get; }

    RectangleF? CaretBounds { get; }

    TextCandidatePresentation CandidatePresentation { get; }

    void Select(TextRange selection);

    void Replace(TextRange range, string text);

    void SetComposition(TextRange composition, TextRange? target);

    void SetCandidates(TextCandidateList? candidates);
}
