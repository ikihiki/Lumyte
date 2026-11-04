namespace Lumyte.TextEditing;

/// <summary>Stages exclusive document edits until Commit; disposal discards unpublished changes.</summary>
public sealed class TextDocumentTransaction : IDisposable
{
    private readonly TextDocument _document;
    private readonly TextDocument.State _initial;
    private readonly TextEditOrigin _origin;
    private TextDocument.State _state;
    private bool _finished;

    internal TextDocumentTransaction(TextDocument document, TextDocument.State initial, TextEditOrigin origin)
    {
        _document = document;
        _initial = initial;
        _state = initial;
        _origin = origin;
    }

    /// <summary>Capture Snapshot.</summary>
    /// <returns>The operation result.</returns>
    public TextDocumentSnapshot CaptureSnapshot()
    {
        Check();
        return _document.Snapshot(_state);
    }

    /// <summary>Replace.</summary>
    /// <param name = "range">The range.</param>
    /// <param name = "text">The text.</param>
    /// <param name = "selection">The selection.</param>
    public void Replace(TextSpan range, string text, TextSelection selection)
    {
        Check();
        TextDocument.ValidateRange(_state.Text, range);
        string replacement = TextDocument.Normalize(text);
        string result = _state.Text.Remove(range.Start, range.Length).Insert(range.Start, replacement);
        _document.ValidateText(result);
        TextDocument.ValidateSelection(result, selection);
        TextCompositionSnapshot? composition = _state.Composition;
        if (composition is not null)
        {
            int start = composition.Range.Start;
            int end = checked(start + composition.Range.Length);
            int editEnd = range.Start + range.Length;
            int delta = replacement.Length - range.Length;
            if (editEnd <= start && range.Start != start)
            {
                composition = composition with
                {
                    Range = new(start + delta, composition.Range.Length),
                    Spans = Array.Empty<TextCompositionSpan>(),
                };
            }
            else if (range.Start >= start && editEnd <= end)
            {
                composition = composition with
                {
                    Range = new(start, composition.Range.Length + delta),
                    Spans = Array.Empty<TextCompositionSpan>(),
                };
            }
            else
            {
                throw new InvalidOperationException("An edit cannot cross the pending composition boundary.");
            }
        }

        string committed = composition is null ? result : result.Remove(composition.Range.Start, composition.Range.Length).Insert(composition.Range.Start, _state.OriginalPending);
        _document.ValidateText(committed);
        _state = _state with
        {
            Text = result,
            Committed = committed,
            Selection = selection,
            Composition = composition,
        };
    }

    /// <summary>Set Selection.</summary>
    /// <param name = "selection">The selection.</param>
    public void SetSelection(TextSelection selection)
    {
        Check();
        TextDocument.ValidateSelection(_state.Text, selection);
        _state = _state with
        {
            Selection = selection,
        };
    }

    /// <summary>Set Composition.</summary>
    /// <param name = "composition">The composition.</param>
    public void SetComposition(TextCompositionSnapshot? composition)
    {
        Check();
        if (composition is null)
        {
            EndComposition(TextCompositionEnd.Commit);
            return;
        }

        TextDocument.ValidateRange(_state.Text, composition.Range);
        if (composition.Id == Guid.Empty || composition.Spans.Count > 256)
        {
            throw new ArgumentException("Invalid composition identity or span limit.", nameof(composition));
        }

        foreach (TextCompositionSpan span in composition.Spans)
        {
            TextDocument.ValidateRange(_state.Text, span.Range);
            if (span.Range.Start < composition.Range.Start || span.Range.Start + span.Range.Length > composition.Range.Start + composition.Range.Length)
            {
                throw new ArgumentException("Attributes must lie inside the composition.", nameof(composition));
            }
        }

        string pending;
        TextSelection checkpoint;
        if (_state.Composition is { } previous)
        {
            if (previous.Id != composition.Id)
            {
                throw new InvalidOperationException("End the current composition before starting another.");
            }

            int prefix = Math.Max(0, composition.Range.Start - previous.Range.Start);
            int suffix = Math.Max(0, previous.Range.Start + previous.Range.Length - composition.Range.Start - composition.Range.Length);
            int skip = Math.Min(prefix, _state.OriginalPending.Length);
            int keep = Math.Max(0, _state.OriginalPending.Length - skip - suffix);
            pending = _state.OriginalPending.Substring(skip, keep);
            checkpoint = new(composition.Range.Start + pending.Length, composition.Range.Start + pending.Length);
        }
        else
        {
            int start = Math.Min(_initial.Selection.Anchor, _initial.Selection.Active);
            int length = Math.Abs(_initial.Selection.Active - _initial.Selection.Anchor);
            pending = _state.Text == _initial.Text ? _initial.Text.Substring(composition.Range.Start, composition.Range.Length) : _initial.Text.Substring(start, length);
            checkpoint = _initial.Selection;
        }

        string committed = _state.Text.Remove(composition.Range.Start, composition.Range.Length).Insert(composition.Range.Start, pending);
        _document.ValidateText(committed);
        TextDocument.ValidateSelection(committed, checkpoint);
        _state = _state with
        {
            Composition = composition with
            {
                Spans = Array.AsReadOnly(composition.Spans.ToArray()),
            },
            Committed = committed,
            CheckpointSelection = checkpoint,
            OriginalPending = pending,
        };
    }

    /// <summary>End Composition.</summary>
    /// <param name = "behavior">The behavior.</param>
    public void EndComposition(TextCompositionEnd behavior)
    {
        Check();
        if (_state.Composition is null)
        {
            return;
        }

        bool cancel = behavior == TextCompositionEnd.Cancel;
        _state = _state with
        {
            Text = cancel ? _state.Committed : _state.Text,
            Committed = cancel ? _state.Committed : _state.Text,
            Selection = cancel ? _state.CheckpointSelection ?? new(_state.Committed.Length, _state.Committed.Length) : _state.Selection,
            Composition = null,
            CheckpointSelection = null,
            OriginalPending = string.Empty,
        };
    }

    /// <summary>Commit.</summary>
    public void Commit()
    {
        Check();
        _finished = true;
        _document.Finish(this, _state, _origin);
    }

    /// <summary>Dispose.</summary>
    public void Dispose()
    {
        if (!_finished)
        {
            _finished = true;
            _document.Finish(this, null, _origin);
        }
    }

    private void Check() => ObjectDisposedException.ThrowIf(_finished, this);
}
