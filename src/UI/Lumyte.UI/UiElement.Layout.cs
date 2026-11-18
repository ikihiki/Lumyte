using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using WidgetDefinition = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>A persistent UI instance with independent configuration, topology and lifetime.</summary>
public partial class UiElement
{
    private bool _layoutDirty = true;
    private bool _layoutCacheValid;
    private UiBoxConstraints _layoutConstraints;
    private Vector2 _size;
    private Rect2D _bounds;
    private (Vector2 Size, Rect2D Bounds, UiBoxConstraints? Constraints)? _preparedLayout;
    private WidgetDefinition? _preparedLayoutWidget;
    private UiElement[]? _preparedLayoutChildren;

    /// <summary>Gets the last computed size; a failed pass may leave partial results.</summary>
    public Vector2 Size => PreparationSurface?.LayoutPass is not null ? PreparedSize : _size;

    /// <summary>Gets the current bounds in the parent's logical coordinates.</summary>
    public Rect2D Bounds => PreparationSurface?.LayoutPass is not null ? _preparedLayout?.Bounds ?? _bounds : _bounds;

    internal Vector2 PreparedSize => _preparedLayout?.Size ?? _size;

    internal bool IsLayoutDirty => _layoutDirty;

    internal long LayoutPassId { get; set; }

    internal bool LayoutComplete { get; set; }

    internal bool LayoutOffsetSet { get; set; }

    internal bool CanReuseLayout(UiBoxConstraints constraints) => PreparationSurface?.IsPreparingViewportLayout == true
        ? (_preparedLayout?.Constraints == constraints && ReferenceEquals(_preparedLayoutWidget, Widget) && _preparedLayoutChildren is not null && _preparedLayoutChildren.SequenceEqual(Children))
            || (_preparedLayout is null && !_layoutDirty && _layoutCacheValid && _layoutConstraints == constraints && ReferenceEquals(_layoutPreparationWidget, _widget))
        : !_layoutDirty && _layoutCacheValid && _layoutConstraints == constraints;

    internal void CacheLayout(UiBoxConstraints constraints)
    {
        if (_preparedLayout is { } prepared)
        {
            _preparedLayout = (prepared.Size, prepared.Bounds, constraints);
            _preparedLayoutWidget = Widget;
            _preparedLayoutChildren = Children.ToArray();
            return;
        }

        _layoutConstraints = constraints;
        _layoutCacheValid = true;
    }

    internal void InvalidateLayoutCache() => _layoutCacheValid = false;

    internal Vector2 InvokeLayout(UiBoxConstraints constraints) => LayoutOverride(constraints);

    internal void PublishLayout(Vector2 size, Vector2 offset)
    {
        if (PreparationSurface?.IsPreparingViewportLayout == true)
        {
            _preparedLayout = (size, new(offset.X, offset.Y, size.X, size.Y), _preparedLayout?.Constraints);
            return;
        }

        _size = size;
        _bounds = new(offset.X, offset.Y, size.X, size.Y);
        _layoutDirty = false;
    }

    internal void CompletePreparedLayout(bool commit)
    {
        if (_preparedLayout is { } prepared && commit)
        {
            _size = prepared.Size;
            _bounds = prepared.Bounds;
            if (prepared.Constraints is { } constraints)
            {
                _layoutConstraints = constraints;
                _layoutCacheValid = true;
            }

            _layoutDirty = false;
        }

        _preparedLayout = null;
        _preparedLayoutWidget = null;
        _preparedLayoutChildren = null;
        CompletePreparedContentGeometry(commit);
    }

    internal void InvalidatePreparedLayout()
    {
        if (_preparedLayout is { } prepared)
        {
            _preparedLayout = (prepared.Size, prepared.Bounds, null);
        }
    }

    internal void MarkLayoutDirty()
    {
        for (UiElement? element = this; element is not null; element = element.Parent)
        {
            element._layoutDirty = true;
        }
    }

    internal UiInvalidation ConfigurationChanges(WidgetDefinition next)
    {
        UiInvalidation result = ReferenceEquals(Widget, next) ? UiInvalidation.None : CompareConfiguration(Widget, next);
        if (Widget.Width != next.Width || Widget.Height != next.Height || Widget.IsVisible != next.IsVisible || Widget.IsPresent != next.IsPresent)
        {
            result |= UiInvalidation.Layout;
        }

        if (Widget.IsEnabled != next.IsEnabled || Widget.HitTestVisible != next.HitTestVisible || Widget.Focusable != next.Focusable)
        {
            result |= UiInvalidation.Input;
        }

        if (Widget.ClipToBounds != next.ClipToBounds || Widget.SharedElementKey != next.SharedElementKey)
        {
            result |= UiInvalidation.Input | UiInvalidation.Visual;
        }

        return result | ModifierChanges(next);
    }

    /// <summary>Invalidates cached layout after component-owned state changes, including every dependent ancestor.</summary>
    protected void InvalidateLayout() => MarkLayoutDirty();

    /// <summary>Classifies component-specific configuration differences; common fields are checked separately.</summary>
    /// <param name="previous">The current configuration.</param>
    /// <param name="next">The proposed configuration.</param>
    /// <returns>The runtime work required after publication.</returns>
    protected virtual UiInvalidation CompareConfiguration(WidgetDefinition previous, WidgetDefinition next) => UiInvalidation.All;

    /// <summary>Computes a finite size within the dimension-adjusted constraints.</summary>
    /// <param name="constraints">The effective constraints.</param>
    /// <returns>The final size.</returns>
    protected virtual Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(Vector2.Zero);

    /// <summary>Lays out a direct child once in the current pass.</summary>
    /// <param name="child">The runtime child instance.</param>
    /// <param name="constraints">The child's constraints.</param>
    /// <returns>The computed size.</returns>
    protected Vector2 LayoutChild(UiElement child, UiBoxConstraints constraints) => GetLayoutPass().LayoutChild(this, child, constraints);

    /// <summary>Positions a laid-out child once without changing its size.</summary>
    /// <param name="child">The runtime child instance.</param>
    /// <param name="offset">The finite parent-local offset.</param>
    protected void SetChildOffset(UiElement child, Vector2 offset) => GetLayoutPass().SetChildOffset(this, child, offset);

    private UiLayoutPass GetLayoutPass() => PreparationSurface?.LayoutPass ?? throw new InvalidOperationException("Layout helpers require an active layout pass.");
}
