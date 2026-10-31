using System.Numerics;
using Lumyte.Composition;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing;

/// <summary>Defines compose.</summary>
public static partial class Compose
{
    /// <summary>Defines definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>A controlled single-line editor with an owned document and a borrowed layout provider.</summary>
        [Composable]
        public partial class TextBox : Lumyte.UI.Compose.Definitions.Widget, ITextDocumentNavigator
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

            /// <summary>Initializes a new instance of the <see cref = "TextBox"/> class.</summary>
            public TextBox()
            {
                Focusable = true;
                Role = UiRole.TextBox;
                ClipToBounds = true;
            }

            /// <summary>Gets or sets the controlled committed value supplied by the application.</summary>
            [ComposeParameter]
            public string Text { get; set; } = string.Empty;

            /// <summary>Gets the implementation-owned editing document for low-level text integration.</summary>
            public TextDocument Document
            {
                get
                {
                    if (_document is null)
                    {
                        _document = new(Text);
                        _lastDeclaredText = Text;
                        _textPending = false;
                    }

                    return _document;
                }
            }

            /// <summary>Gets or sets the layout provider.</summary>
            [ComposeParameter]
            public IUiTextLayoutProvider? LayoutProvider { get; set; }

            /// <summary>Gets or sets a value indicating whether editing is disabled.</summary>
            [ComposeParameter]
            public bool IsReadOnly { get; set; }

            /// <summary>Gets or sets the on text changed.</summary>
            [ComposeParameter]
            public Action<UiTextChange>? OnTextChanged { get; set; }

            /// <summary>Gets or sets the on composition changed.</summary>
            [ComposeParameter]
            public Action<TextCompositionSnapshot?>? OnCompositionChanged { get; set; }

            /// <summary>Gets the current layout.</summary>
            public UiTextLayout? CurrentLayout => _layout;

            /// <summary>Gets the horizontal offset.</summary>
            public float HorizontalOffset => _offset;

            internal IUiTextInputHandler? InputHandler => UiTextInputScope.Resolve(ConnectedSurface);

            internal bool SourceUpdatesBlocked { get; set; }

            internal ulong SourceRevision => _sourceRevision;

            internal bool HasSourceValueChange => (_textPending || _lastDeclaredText != Text) && TextDocument.Normalize(Text) != Document.CaptureSnapshot().CommittedText;

            /// <summary>Gets a value indicating whether the editing state can continue captured input.</summary>
            protected override bool RetainsInputOnRebuild => true;

            /// <summary>Gets the automation actions.</summary>
            protected override UiAutomationActions AutomationActions => UiAutomationActions.Focus | UiAutomationActions.Edit;

            /// <summary>Navigate.</summary>
            /// <param name = "document">The document.</param>
            /// <param name = "direction">The direction.</param>
            /// <param name = "extend">The extend.</param>
            /// <returns>The operation result.</returns>
            public TextSelection Navigate(TextDocumentSnapshot document, TextNavigation direction, bool extend)
            {
                if (LayoutProvider is null)
                {
                    return document.Selection;
                }

                UiTextLayout layout = LayoutProvider.CreateLayout(document, float.PositiveInfinity);
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

                if (!_textPending && _lastDeclaredText == Text)
                {
                    return true;
                }

                string value = TextDocument.Normalize(Text);
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

                _lastDeclaredText = Text;
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

            /// <summary>Layout Override.</summary>
            /// <param name = "constraints">The constraints.</param>
            /// <returns>The operation result.</returns>
            protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
            {
                SynchronizeText();
                if (Document is null || LayoutProvider is null)
                {
                    _layout = null;
                    return constraints.Constrain(Vector2.Zero);
                }

                TextDocumentSnapshot snapshot = Document.CaptureSnapshot();
                float width = constraints.MaxWidth;
                if (_layout is null || _layoutRevision != snapshot.Revision || _layoutWidth != width || !ReferenceEquals(Document, _layoutDocument) || !ReferenceEquals(LayoutProvider, _layoutProvider))
                {
                    // A single-line editor shapes without wrapping; its viewport clips the overflow.
                    _layout = LayoutProvider.CreateLayout(snapshot, float.PositiveInfinity);
                    if (_layout.DocumentRevision != snapshot.Revision)
                    {
                        throw new InvalidOperationException("The text layout does not match the document revision.");
                    }

                    _layoutDocument = Document;
                    _layoutProvider = LayoutProvider;
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

            /// <summary>Retains the logical editor's document, selection, composition and scroll state.</summary>
            /// <param name="previous">The previous declaration of the same editor.</param>
            protected override void OnReconcile(Lumyte.UI.Compose.Definitions.Widget previous)
            {
                var editor = (TextBox)previous;
                _document = editor.Document;
                _lastDeclaredText = editor._lastDeclaredText;
                _sourceRevision = editor._sourceRevision;
                SourceUpdatesBlocked = editor.SourceUpdatesBlocked;
                _offset = editor._offset;
                _focused = editor._focused;
                _textPending = true;
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
    }
}
