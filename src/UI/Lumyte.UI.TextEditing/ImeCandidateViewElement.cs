using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using Lumyte.TextEditing;

namespace Lumyte.UI.TextEditing;

/// <summary>Owns candidate layout and pointer state without taking keyboard focus.</summary>
public class ImeCandidateViewElement : UiElement<Compose.Definitions.ImeCandidateView>
{
    private readonly List<Row> _rows = [];
    private ImeCandidateSnapshot? _candidates;
    private ImeCandidateAction? _pressed;

    /// <summary>Initializes a new instance of the <see cref="ImeCandidateViewElement"/> class.</summary>
    /// <param name="widget">The initial configuration.</param>
    protected internal ImeCandidateViewElement(Compose.Definitions.ImeCandidateView widget)
        : base(widget)
    {
    }

    /// <summary>Gets the currently copied candidates.</summary>
    public ImeCandidateSnapshot? Candidates => _candidates;

    /// <summary>Gets a value indicating whether this option is enabled.</summary>
    protected sealed override bool CanReceiveFocus => false;

    /// <summary>Gets the automation actions.</summary>
    protected sealed override UiAutomationActions AutomationActions => UiAutomationActions.None;

    /// <summary>Gets the automation exposure.</summary>
    protected sealed override UiAutomationExposure AutomationExposure => UiAutomationExposure.RedactedSubtree;

    internal void SetCandidates(ImeCandidateSnapshot? snapshot)
    {
        _candidates = snapshot is null ? null : ImeCandidateValidation.Copy(snapshot);
        _pressed = null;
        InvalidateLayout();
        InvalidateInput();
    }

    /// <inheritdoc/>
    protected override void OnMounted() => SetCandidates(Widget.Candidates);

    /// <inheritdoc/>
    protected override void OnWidgetUpdated(Compose.Definitions.ImeCandidateView previous) => SetCandidates(Widget.Candidates);

    /// <summary>On Probe Pointer Down.</summary>
    /// <param name = "localPosition">The local position.</param>
    /// <returns>The operation result.</returns>
    protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => UiInputResult.Handled;

    /// <summary>On Input.</summary>
    /// <param name = "context">The context.</param>
    /// <returns>The operation result.</returns>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is not UiPointerInput pointer)
        {
            return UiInputResult.Unhandled;
        }

        if (pointer.Phase == UiPointerPhase.Down)
        {
            _pressed = context.PointerPosition is { } at ? ActionAt(at) : null;
            context.CapturePointer();
        }
        else if (pointer.Phase == UiPointerPhase.Up && context.IsCaptured)
        {
            ImeCandidateAction? pressed = _pressed;
            _pressed = null;
            ImeCandidateAction? current = context.PointerPosition is { } at ? ActionAt(at) : null;
            if (pressed is not null && pressed == current)
            {
                Widget.OnAction?.Invoke(pressed);
            }
        }
        else if (pointer.Phase == UiPointerPhase.Cancel)
        {
            _pressed = null;
        }

        return UiInputResult.Handled;
    }

    /// <summary>Layout Override.</summary>
    /// <param name = "constraints">The constraints.</param>
    /// <returns>The operation result.</returns>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        _rows.Clear();
        if (!float.IsFinite(Widget.Style.Padding) || Widget.Style.Padding < 0 || !float.IsFinite(Widget.Style.RowSpacing) || Widget.Style.RowSpacing < 0)
        {
            throw new ArgumentException("Candidate spacing must be finite and nonnegative.");
        }

        if (_candidates is null || Widget.LayoutProvider is null)
        {
            return constraints.Constrain(Vector2.Zero);
        }

        float width = float.IsFinite(constraints.MaxWidth) ? constraints.MaxWidth : 320;
        float height = float.IsFinite(constraints.MaxHeight) ? constraints.MaxHeight : 240;
        float y = Widget.Style.Padding;
        float contentWidth = MathF.Max(0, width - (Widget.Style.Padding * 2));
        int page = _candidates.CurrentPage ?? 0;
        int start = _candidates.PageStarts.Count == 0 ? 0 : _candidates.PageStarts[page];
        int end = page + 1 < _candidates.PageStarts.Count ? _candidates.PageStarts[page + 1] : _candidates.Items.Count;
        for (int index = start; index < end; index++)
        {
            string text = _candidates.Items[index].Text;
            UiTextLayout layout = Widget.LayoutProvider.CreateLayout(new(0, text, text, default, null), float.PositiveInfinity);
            float rowHeight = MathF.Max(20, layout.Size.Y);
            if (y + rowHeight > height - Widget.Style.Padding - 26)
            {
                break;
            }

            _rows.Add(new(new(Widget.Style.Padding, y, contentWidth, rowHeight), layout, ImeCandidateActionKind.Select, index));
            y += rowHeight + Widget.Style.RowSpacing;
        }

        string[] labels = ["前", "次", "確定", "取消"];
        ImeCandidateActionKind[] kinds = [ImeCandidateActionKind.Page, ImeCandidateActionKind.Page, ImeCandidateActionKind.Finalize, ImeCandidateActionKind.Abort];
        int?[] indices = [page - 1, page + 1, null, null];
        float footerWidth = contentWidth / 4;
        for (int part = 0; part < 4; part++)
        {
            UiTextLayout layout = Widget.LayoutProvider.CreateLayout(new(0, labels[part], labels[part], default, null), float.PositiveInfinity);
            _rows.Add(new(new(Widget.Style.Padding + (footerWidth * part), y, footerWidth, MathF.Max(22, layout.Size.Y)), layout, kinds[part], indices[part]));
        }

        return constraints.Constrain(new(width, y + 26 + Widget.Style.Padding));
    }

    /// <summary>On Record.</summary>
    /// <param name = "canvas">The canvas.</param>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        canvas.FillRect(new(0, 0, Size.X, Size.Y), Widget.Style.Background);
        foreach (Row row in _rows)
        {
            bool selected = row.Kind == ImeCandidateActionKind.Select && _candidates?.SelectedIndex == row.Index;
            if (selected)
            {
                canvas.FillRect(row.Bounds, Widget.Style.SelectedBackground);
            }

            canvas.Save();
            try
            {
                canvas.Translate(new(row.Bounds.X, row.Bounds.Y));
                foreach (TextDrawing2D drawing in row.Layout.Drawing.Drawings)
                {
                    if (drawing is TextGeometry2D glyph)
                    {
                        canvas.FillPath(glyph.Path, selected ? Widget.Style.SelectedForeground : Widget.Style.Foreground);
                    }
                    else if (drawing is TextPaintGeometry2D paint)
                    {
                        canvas.FillRect(paint.Bounds, paint.Paint);
                    }
                }
            }
            finally
            {
                canvas.Restore();
            }
        }
    }

    private ImeCandidateAction? ActionAt(Vector2 point)
    {
        if (_candidates is not { } candidates)
        {
            return null;
        }

        Row? row = _rows.FirstOrDefault(r => point.X >= r.Bounds.X && point.X < r.Bounds.X + r.Bounds.Width && point.Y >= r.Bounds.Y && point.Y < r.Bounds.Y + r.Bounds.Height);
        if (row is null)
        {
            return null;
        }

        ImeCandidateOperations operation = row.Kind switch
        {
            ImeCandidateActionKind.Select => ImeCandidateOperations.Select,
            ImeCandidateActionKind.Page => ImeCandidateOperations.Page,
            ImeCandidateActionKind.Finalize => ImeCandidateOperations.Finalize,
            _ => ImeCandidateOperations.Abort,
        };
        if ((candidates.Operations & operation) == 0 || (row.Kind == ImeCandidateActionKind.Page && (row.Index < 0 || row.Index >= candidates.PageStarts.Count)) || (row.Kind == ImeCandidateActionKind.Finalize && candidates.SelectedIndex is null))
        {
            return null;
        }

        return new(candidates.Id, candidates.Revision, row.Kind, row.Index);
    }

    private sealed record Row(Rect2D Bounds, UiTextLayout Layout, ImeCandidateActionKind Kind, int? Index);
}
