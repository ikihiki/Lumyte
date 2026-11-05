using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>A persistent UI instance with independent geometry.</summary>
public partial class UiElement
{
    private Rect2D[] _modifierBounds = [];

    /// <summary>Gets the innermost body rectangle relative to the outer origin.</summary>
    public Rect2D ContentBounds { get; private set; }

    /// <summary>Gets the size available to component drawing and input.</summary>
    public Vector2 ContentSize => new(ContentBounds.Width, ContentBounds.Height);

    internal ulong ModifierGeometryRevision { get; private set; }

    internal Vector2 ContentOffset => new(ContentBounds.X, ContentBounds.Y);

    internal Vector2 LayoutModified(UiBoxConstraints constraints)
    {
        ReadOnlySpan<UiBoxModifier> modifiers = Widget.BoxModifiers;
        var parents = new UiBoxConstraints[modifiers.Length];
        for (int i = 0; i < modifiers.Length; i++)
        {
            parents[i] = constraints;
            constraints = modifiers[i].Forward(constraints);
        }

        constraints = constraints.Tighten(Widget.Width, Widget.Height);
        Vector2 body = InvokeLayout(constraints);
        if (!float.IsFinite(body.X) || !float.IsFinite(body.Y)
            || body.X < constraints.MinWidth || body.X > constraints.MaxWidth
            || body.Y < constraints.MinHeight || body.Y > constraints.MaxHeight)
        {
            throw new InvalidOperationException("Layout must return a finite size within its constraints.");
        }

        Vector2 size = body;
        var offsets = new Vector2[modifiers.Length];
        var rectangles = new Rect2D[modifiers.Length];
        for (int i = modifiers.Length - 1; i >= 0; i--)
        {
            size = modifiers[i].Backward(parents[i], size, out offsets[i]);
            rectangles[i] = new(0, 0, size.X, size.Y);
        }

        Vector2 origin = Vector2.Zero;
        for (int i = 0; i < modifiers.Length; i++)
        {
            rectangles[i] = new(origin.X, origin.Y, rectangles[i].Width, rectangles[i].Height);
            origin = new(UiLayoutMath.FiniteFloat((double)origin.X + offsets[i].X), UiLayoutMath.FiniteFloat((double)origin.Y + offsets[i].Y));
        }

        _ = UiLayoutMath.FiniteFloat((double)origin.X + body.X);
        _ = UiLayoutMath.FiniteFloat((double)origin.Y + body.Y);
        PublishContentGeometry(new(origin.X, origin.Y, body.X, body.Y), rectangles);
        return size;
    }

    internal void ResetContentGeometry() => PublishContentGeometry(default, []);

    internal void RecordModified(DrawingCanvas2D canvas, bool selfOnly = false)
    {
        canvas.Save();
        try
        {
            for (int i = 0; i < _modifierBounds.Length; i++)
            {
                UiBoxModifier modifier = Widget.BoxModifiers[i];
                if (modifier.Kind != UiBoxModifierKind.Border || modifier.Left == 0 || modifier.Color.A == 0)
                {
                    continue;
                }

                Rect2D r = _modifierBounds[i];
                float x = Math.Min(modifier.Left, r.Width);
                float y = Math.Min(modifier.Left, r.Height);
                float bottomY = UiLayoutMath.FiniteFloat((double)r.Y + Math.Max(y, r.Height - y));
                float sideY = UiLayoutMath.FiniteFloat((double)r.Y + y);
                float rightX = UiLayoutMath.FiniteFloat((double)r.X + Math.Max(x, r.Width - x));
                canvas.FillRect(new(r.X, r.Y, r.Width, y), modifier.Color);
                canvas.FillRect(new(r.X, bottomY, r.Width, Math.Max(0, Math.Min(y, r.Height - y))), modifier.Color);
                float middle = (float)Math.Max(0, (double)r.Height - (2d * y));
                canvas.FillRect(new(r.X, sideY, x, middle), modifier.Color);
                canvas.FillRect(new(rightX, sideY, Math.Max(0, Math.Min(x, r.Width - x)), middle), modifier.Color);
            }

            canvas.Translate(ContentOffset);
            if (selfOnly)
            {
                InvokeRecordSelf(canvas);
            }
            else
            {
                InvokeRecord(canvas);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    internal UiInvalidation ModifierChanges(Compose.Definitions.Widget next)
    {
        ReadOnlySpan<UiBoxModifier> before = Widget.BoxModifiers;
        ReadOnlySpan<UiBoxModifier> after = next.BoxModifiers;
        if (before.Length != after.Length)
        {
            return UiInvalidation.All;
        }

        UiInvalidation result = UiInvalidation.None;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i])
            {
                result |= before[i] with { Color = default } == after[i] with { Color = default } ? UiInvalidation.Visual : UiInvalidation.All;
            }
        }

        return result;
    }

    private void PublishContentGeometry(Rect2D content, Rect2D[] rectangles)
    {
        if (ContentBounds != content || !_modifierBounds.AsSpan().SequenceEqual(rectangles))
        {
            ModifierGeometryRevision = checked(ModifierGeometryRevision + 1);
        }

        ContentBounds = content;
        _modifierBounds = rectangles;
    }
}
