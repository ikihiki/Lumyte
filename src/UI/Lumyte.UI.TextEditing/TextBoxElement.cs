using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing;

/// <summary>Owns editing, selection, composition, layout and scrolling across declaration rebuilds.</summary>
public class TextBoxElement : UiElement<Compose.Definitions.TextBox>, ITextDocumentNavigator
{
    private TextDocument? _document;
    private string? _lastDeclaredText;
    private bool _textPending = true;
    private ulong _sourceRevision;
    private TextDocument? _layoutDocument;
    private IUiTextLayoutProvider? _layoutProvider;
    private UiTextLayout? _layout;
    private ulong _layoutRevision = ulong.MaxValue;
    private float _layoutWidth = float.NaN;
    private float _offset;
    private bool _focused;

    /// <summary>Initializes a new instance of the <see cref="TextBoxElement"/> class.</summary>
    /// <param name="widget">The initial controlled editor configuration.</param>
    protected internal TextBoxElement(Compose.Definitions.TextBox widget)
        : base(widget)
    {
    }

    /// <summary>Gets the implementation-owned editing document for low-level text integration.</summary>
    public TextDocument Document
    {
        get
        {
            if (_document is null)
            {
                _document = new(Widget.Text);
                _lastDeclaredText = Widget.Text;
                _textPending = false;
            }

            return _document;
        }
    }

    /// <summary>Gets the current layout.</summary>
    public UiTextLayout? CurrentLayout => _layout;

    /// <summary>Gets the horizontal offset.</summary>
    public float HorizontalOffset => _offset;

    internal IUiTextInputHandler? InputHandler => UiTextInputScope.Resolve(Surface);

    internal bool SourceUpdatesBlocked { get; set; }

    internal ulong SourceRevision => _sourceRevision;

    internal bool HasSourceValueChange => (_textPending || _lastDeclaredText != Widget.Text) && TextDocument.Normalize(Widget.Text) != Document.CaptureSnapshot().CommittedText;

    /// <inheritdoc/>
    protected override bool RequiresHostValidation => true;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.Focus | UiAutomationActions.Edit;

    /// <summary>Navigate.</summary>
    /// <param name = "document">The document.</param>
    /// <param name = "direction">The direction.</param>
    /// <param name = "extend">The extend.</param>
    /// <returns>The operation result.</returns>
    public TextSelection Navigate(TextDocumentSnapshot document, TextNavigation direction, bool extend)
    {
        if (Widget.LayoutProvider is null)
        {
            return document.Selection;
        }

        UiTextLayout layout = Widget.LayoutProvider.CreateLayout(document, float.PositiveInfinity);
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

    internal bool SynchronizeText()
    {
        TextDocument document = Document;
        if (SourceUpdatesBlocked)
        {
            return false;
        }

        if (!_textPending && _lastDeclaredText == Widget.Text)
        {
            return true;
        }

        string value = TextDocument.Normalize(Widget.Text);
        TextDocumentSnapshot snapshot = document.CaptureSnapshot();
        if (snapshot.CommittedText != value)
        {
            if (snapshot.Composition is not null)
            {
                return false;
            }

            int Clamp(int position)
            {
                int clamped = Math.Clamp(position, 0, value.Length);
                return clamped > 0 && clamped < value.Length && char.IsLowSurrogate(value[clamped]) ? clamped - 1 : clamped;
            }

            TextSelection selection = snapshot.Selection;
            if (!document.TryReplace(snapshot.Revision, new(0, snapshot.Text.Length), value, new(Clamp(selection.Anchor), Clamp(selection.Active), selection.AnchorAffinity, selection.ActiveAffinity), TextEditOrigin.Application))
            {
                return false;
            }

            _sourceRevision = document.CaptureSnapshot().Revision;
            DocumentChanged();
        }

        _lastDeclaredText = Widget.Text;
        _textPending = false;
        return true;
    }

    internal void DocumentChanged()
    {
        _layoutRevision = ulong.MaxValue;
        InvalidateLayout();
    }

    internal UiTextCaret HitCaret(Vector2 position) => _layout?.HitTest(position + new Vector2(_offset, 0)) ?? default;

    internal void SetPointerSelection(Vector2 position, bool extend)
    {
        if (Document is null || _layout is null)
        {
            return;
        }

        TextDocumentSnapshot snapshot = Document.CaptureSnapshot();
        UiTextCaret caret = HitCaret(position);
        Document.TrySetSelection(snapshot.Revision, new(extend ? snapshot.Selection.Anchor : caret.Position, caret.Position, extend ? snapshot.Selection.AnchorAffinity : caret.Affinity, caret.Affinity), TextEditOrigin.Keyboard);
    }

    /// <inheritdoc/>
    protected override bool HostServicesChanged(Lumyte.UI.Compose.Definitions.Widget previous, Lumyte.UI.Compose.Definitions.Widget next)
    {
        var before = (Compose.Definitions.TextBox)previous;
        var after = (Compose.Definitions.TextBox)next;
        return before.IsReadOnly != after.IsReadOnly || !ReferenceEquals(before.LayoutProvider, after.LayoutProvider);
    }

    /// <inheritdoc/>
    protected override void OnWidgetUpdated(Compose.Definitions.TextBox previous) => _textPending = true;

    /// <summary>Layout Override.</summary>
    /// <param name = "constraints">The constraints.</param>
    /// <returns>The operation result.</returns>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        SynchronizeText();
        if (Document is null || Widget.LayoutProvider is null)
        {
            _layout = null;
            return constraints.Constrain(Vector2.Zero);
        }

        TextDocumentSnapshot snapshot = Document.CaptureSnapshot();
        float width = constraints.MaxWidth;
        if (_layout is null || _layoutRevision != snapshot.Revision || _layoutWidth != width || !ReferenceEquals(Document, _layoutDocument) || !ReferenceEquals(Widget.LayoutProvider, _layoutProvider))
        {
            // A single-line editor shapes without wrapping; its viewport clips the overflow.
            _layout = Widget.LayoutProvider.CreateLayout(snapshot, float.PositiveInfinity);
            if (_layout.DocumentRevision != snapshot.Revision)
            {
                throw new InvalidOperationException("The text layout does not match the document revision.");
            }

            _layoutDocument = Document;
            _layoutProvider = Widget.LayoutProvider;
            _layoutRevision = snapshot.Revision;
            _layoutWidth = width;
        }

        Vector2 size = constraints.Constrain(_layout.Size);
        Rect2D caret = _layout.GetCaretBounds(new(snapshot.Selection.Active, snapshot.Selection.ActiveAffinity));
        float right = caret.X + caret.Width;
        _offset = Math.Clamp(_offset, 0, MathF.Max(0, _layout.Size.X - size.X + 1));
        if (right - _offset > size.X)
        {
            _offset = MathF.Max(0, right - size.X);
        }
        else if (caret.X < _offset)
        {
            _offset = caret.X;
        }

        return size;
    }

    /// <summary>On Probe Pointer Down.</summary>
    /// <param name = "localPosition">The local position.</param>
    /// <returns>The operation result.</returns>
    protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => UiInputResult.Handled;

    /// <summary>On Input.</summary>
    /// <param name = "context">The context.</param>
    /// <returns>The operation result.</returns>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is UiFocusChangedInput focus)
        {
            _focused = focus.IsFocused;
        }

        if (InputHandler is { } handler)
        {
            return handler.HandleInput(this, context);
        }

        if (context.Input is UiPointerInput pointer)
        {
            if (pointer.Phase == UiPointerPhase.Down)
            {
                context.RequestFocus();
                context.CapturePointer();
            }

            return UiInputResult.Handled;
        }

        return context.Input is UiKeyInput ? UiInputResult.Handled : UiInputResult.Unhandled;
    }

    /// <summary>On Pointer Capture Changed.</summary>
    /// <param name = "state">The state.</param>
    protected override void OnPointerCaptureChanged(UiPointerCaptureState state) => InputHandler?.OnPointerCaptureChanged(this, state);

    /// <summary>On Record.</summary>
    /// <param name = "canvas">The canvas.</param>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        if (_layout is null || Document is null)
        {
            return;
        }

        TextDocumentSnapshot snapshot = Document.CaptureSnapshot();
        if (_layout.DocumentRevision != snapshot.Revision)
        {
            throw new InvalidOperationException("Layout must be updated after editing before recording.");
        }

        canvas.Translate(new(-_offset, 0));
        int start = Math.Min(snapshot.Selection.Anchor, snapshot.Selection.Active);
        int length = Math.Abs(snapshot.Selection.Active - snapshot.Selection.Anchor);
        foreach (Rect2D rect in _layout.GetRangeBounds(new(start, length)))
        {
            canvas.FillRect(rect, new Color2D(0.65f, 0.8f, 1, 1));
        }

        if (snapshot.Composition is { } preedit)
        {
            foreach (TextCompositionSpan selected in preedit.Spans.Where(span => span.Selected))
            {
                foreach (Rect2D rect in _layout.GetRangeBounds(selected.Range))
                {
                    canvas.FillRect(rect, new Color2D(0.65f, 0.8f, 1, 1));
                }
            }
        }

        canvas.DrawText(_layout.Drawing);
        if (snapshot.Composition is { } composition)
        {
            foreach (TextCompositionSpan span in composition.Spans)
            {
                if (span.Underline == TextUnderline.None)
                {
                    continue;
                }

                foreach (Rect2D rect in _layout.GetRangeBounds(span.Range))
                {
                    float height = span.Underline == TextUnderline.Thick ? 2 : 1;
                    float y = rect.Y + MathF.Max(0, rect.Height - height);
                    if (span.Underline == TextUnderline.Dotted)
                    {
                        float right = MathF.Min(rect.X + rect.Width, _offset + Size.X);
                        for (float x = MathF.Max(rect.X, _offset); x < right; x += 4)
                        {
                            canvas.FillRect(new(x, y, MathF.Min(2, right - x), height), new Color2D(0, 0, 0, 1));
                        }
                    }
                    else
                    {
                        canvas.FillRect(new(rect.X, y, rect.Width, height), new Color2D(0, 0, 0, 1));
                    }
                }
            }
        }

        if (_focused)
        {
            canvas.FillRect(_layout.GetCaretBounds(new(snapshot.Selection.Active, snapshot.Selection.ActiveAffinity)), new Color2D(0, 0, 0, 1));
        }
    }
}
