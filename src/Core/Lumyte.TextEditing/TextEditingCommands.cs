namespace Lumyte.TextEditing;

/// <summary>Defines text editing commands.</summary>
public static class TextEditingCommands
{
    /// <summary>Execute.</summary>
    /// <param name = "document">The document.</param>
    /// <param name = "navigator">The navigator.</param>
    /// <param name = "command">The command.</param>
    /// <param name = "extend">The extend.</param>
    /// <param name = "origin">The origin.</param>
    /// <returns>The operation result.</returns>
    public static bool Execute(TextDocument document, ITextDocumentNavigator navigator, TextEditCommand command, bool extend = false, TextEditOrigin origin = TextEditOrigin.Keyboard)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(navigator);
        TextDocumentSnapshot snapshot = document.CaptureSnapshot();
        if (snapshot.Composition is not null)
        {
            return false;
        }

        if (command == TextEditCommand.SelectAll)
        {
            return document.TrySetSelection(snapshot.Revision, new(0, snapshot.Text.Length), origin);
        }

        if (command is TextEditCommand.Previous or TextEditCommand.Next or TextEditCommand.Start or TextEditCommand.End)
        {
            TextNavigation direction = command switch
            {
                TextEditCommand.Previous => TextNavigation.Previous,
                TextEditCommand.Next => TextNavigation.Next,
                TextEditCommand.Start => TextNavigation.Start,
                _ => TextNavigation.End,
            };
            return document.TrySetSelection(snapshot.Revision, navigator.Navigate(snapshot, direction, extend), origin);
        }

        int start = Math.Min(snapshot.Selection.Anchor, snapshot.Selection.Active);
        int end = Math.Max(snapshot.Selection.Anchor, snapshot.Selection.Active);
        if (start == end)
        {
            int[] boundaries = TextDocument.GraphemeBoundaries(snapshot.Text);
            if (command == TextEditCommand.DeleteBackward)
            {
                start = boundaries.LastOrDefault(position => position < start);
            }
            else
            {
                end = boundaries.FirstOrDefault(position => position > end, snapshot.Text.Length);
            }
        }

        return document.TryReplace(snapshot.Revision, new(start, end - start), string.Empty, new(start, start), origin);
    }

    /// <summary>Replaces the entire editable value as one normal document command.</summary>
    /// <param name="document">The target document.</param>
    /// <param name="text">The single-line replacement.</param>
    /// <param name="origin">The cause of editing.</param>
    /// <returns>Whether the current non-composing document accepted the replacement.</returns>
    public static bool ReplaceAll(TextDocument document, string text, TextEditOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(document);
        TextDocumentSnapshot snapshot = document.CaptureSnapshot();
        if (snapshot.Composition is not null)
        {
            return false;
        }

        int caret = TextDocument.Normalize(text).Length;
        return document.TryReplace(snapshot.Revision, new(0, snapshot.Text.Length), text, new(caret, caret), origin);
    }

    /// <summary>Replace Selection.</summary>
    /// <param name = "document">The document.</param>
    /// <param name = "text">The text.</param>
    /// <param name = "origin">The origin.</param>
    /// <returns>The operation result.</returns>
    public static bool ReplaceSelection(TextDocument document, string text, TextEditOrigin origin)
    {
        TextDocumentSnapshot snapshot = document.CaptureSnapshot();
        int start = Math.Min(snapshot.Selection.Anchor, snapshot.Selection.Active);
        int length = Math.Abs(snapshot.Selection.Anchor - snapshot.Selection.Active);
        int caret = checked(start + TextDocument.Normalize(text).Length);
        return document.TryReplace(snapshot.Revision, new(start, length), text, new(caret, caret), origin);
    }
}
