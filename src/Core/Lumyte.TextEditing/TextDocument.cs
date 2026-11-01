using System.Globalization;

namespace Lumyte.TextEditing;

/// <summary>Owns a single-line document with committed text, preedit and atomic revisions.</summary>
public sealed class TextDocument
{
    private readonly List<TextDocumentChange> _changes = [];
    private TextDocumentTransaction? _transaction;
    private State _state;
    private ulong _revision;

    /// <summary>Initializes a new instance of the <see cref = "TextDocument"/> class.</summary>
    /// <param name = "initialText">The initial text.</param>
    /// <param name = "maximumLength">The maximum length.</param>
    public TextDocument(string initialText = "", int maximumLength = 32768)
    {
        if (maximumLength is < 1 or > 1048576)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        MaximumLength = maximumLength;
        string text = Normalize(initialText);
        ValidateText(text);
        _state = new(text, text, new(text.Length, text.Length), null, null, string.Empty);
    }

    /// <summary>Gets the maximum length.</summary>
    public int MaximumLength { get; }

    /// <summary>Normalize.</summary>
    /// <param name = "text">The text.</param>
    /// <returns>The operation result.</returns>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ');
    }

    /// <summary>Grapheme Boundaries.</summary>
    /// <param name = "text">The text.</param>
    /// <returns>The operation result.</returns>
    public static int[] GraphemeBoundaries(string text)
    {
        ValidateUnicode(text);
        return [.. StringInfo.ParseCombiningCharacters(text), text.Length];
    }

    /// <summary>Capture Snapshot.</summary>
    /// <returns>The operation result.</returns>
    public TextDocumentSnapshot CaptureSnapshot() => Snapshot(_state);

    /// <summary>Begin Edit.</summary>
    /// <param name = "origin">The origin.</param>
    /// <returns>The operation result.</returns>
    public TextDocumentTransaction BeginEdit(TextEditOrigin origin)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A document transaction is already active.");
        }

        if (_state.Composition is not null && origin is TextEditOrigin.Application or TextEditOrigin.Automation)
        {
            throw new InvalidOperationException("End composition before replacing the document.");
        }

        _transaction = new(this, _state, origin);
        return _transaction;
    }

    /// <summary>Try Replace.</summary>
    /// <param name = "expectedRevision">The expected revision.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "text">The text.</param>
    /// <param name = "selection">The selection.</param>
    /// <param name = "origin">The origin.</param>
    /// <returns>The operation result.</returns>
    public bool TryReplace(ulong expectedRevision, TextSpan range, string text, TextSelection selection, TextEditOrigin origin)
    {
        if (_transaction is not null || _state.Composition is not null)
        {
            throw new InvalidOperationException("The document is being edited or composed.");
        }

        if (_revision != expectedRevision)
        {
            return false;
        }

        using TextDocumentTransaction edit = BeginEdit(origin);
        edit.Replace(range, text, selection);
        edit.Commit();
        return true;
    }

    /// <summary>Try Set Selection.</summary>
    /// <param name = "expectedRevision">The expected revision.</param>
    /// <param name = "selection">The selection.</param>
    /// <param name = "origin">The origin.</param>
    /// <returns>The operation result.</returns>
    public bool TrySetSelection(ulong expectedRevision, TextSelection selection, TextEditOrigin origin)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("The document is being edited.");
        }

        if (_revision != expectedRevision)
        {
            return false;
        }

        using TextDocumentTransaction edit = BeginEdit(origin);
        edit.SetSelection(selection);
        edit.Commit();
        return true;
    }

    /// <summary>Drain Changes.</summary>
    /// <returns>The operation result.</returns>
    public IReadOnlyList<TextDocumentChange> DrainChanges()
    {
        TextDocumentChange[] changes = _changes.ToArray();
        _changes.Clear();
        return Array.AsReadOnly(changes);
    }

    internal static void ValidateUnicode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (int index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (++index >= text.Length || !char.IsLowSurrogate(text[index]))
                {
                    throw new ArgumentException("Text must contain valid UTF-16.", nameof(text));
                }
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                throw new ArgumentException("Text must contain valid UTF-16.", nameof(text));
            }
        }
    }

    internal static void ValidatePosition(string text, int position)
    {
        if (position < 0 || position > text.Length || (position > 0 && position < text.Length && char.IsHighSurrogate(text[position - 1]) && char.IsLowSurrogate(text[position])))
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
    }

    internal static void ValidateRange(string text, TextSpan range)
    {
        if (range.Length < 0 || range.Start < 0 || range.Start > text.Length - range.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        ValidatePosition(text, range.Start);
        ValidatePosition(text, range.Start + range.Length);
    }

    internal static void ValidateSelection(string text, TextSelection selection)
    {
        ValidatePosition(text, selection.Anchor);
        ValidatePosition(text, selection.Active);
    }

    internal TextDocumentSnapshot Snapshot(State state) => new(_revision, state.Text, state.Committed, state.Selection, state.Composition);

    internal void ValidateText(string text)
    {
        ValidateUnicode(text);
        if (text.Length > MaximumLength)
        {
            throw new ArgumentException("Text exceeds the document limit.", nameof(text));
        }
    }

    internal void Finish(TextDocumentTransaction transaction, State? next, TextEditOrigin origin)
    {
        if (!ReferenceEquals(_transaction, transaction))
        {
            throw new InvalidOperationException("The transaction is no longer active.");
        }

        try
        {
            if (next is null || next == _state)
            {
                return;
            }

            bool committed = next.Committed != _state.Committed;
            bool selection = next.Selection != _state.Selection;
            bool composition = next.Composition != _state.Composition;
            ulong revision = checked(_revision + 1);
            _changes.Add(new(revision, origin, committed, selection, composition));
            _state = next;
            _revision = revision;
        }
        finally
        {
            _transaction = null;
        }
    }

    internal sealed record State(string Text, string Committed, TextSelection Selection, TextCompositionSnapshot? Composition, TextSelection? CheckpointSelection, string OriginalPending);
}
