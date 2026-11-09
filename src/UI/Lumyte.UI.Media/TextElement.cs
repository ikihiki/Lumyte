using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text;
using TextWidget = Lumyte.UI.Media.Compose.Definitions.Text;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Media;

/// <summary>Retains the last successfully completed text snapshot for layout and drawing.</summary>
public class TextElement : UiElement<TextWidget>
{
    private TextLayout2D? _layout;
    private ITextLayoutProvider? _provider;
    private string? _value;
    private float _maximumWidth = float.NaN;

    /// <summary>Initializes a new instance of the <see cref="TextElement"/> class.</summary>
    /// <param name="widget">The initial declaration.</param>
    protected internal TextElement(TextWidget widget)
        : base(widget)
    {
    }

    /// <summary>Gets the last successfully completed snapshot without invoking the provider.</summary>
    public TextLayout2D? CurrentLayout => _layout;

    /// <inheritdoc/>
    protected override bool CacheRetainedDrawing => true;

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => GetType() == typeof(TextElement) && Widget.GetType() == typeof(TextWidget);

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.None;

    /// <inheritdoc/>
    protected override bool CanReceiveFocus => false;

    /// <inheritdoc/>
    // A declaration-only hook cannot compare both completed width-dependent layouts.
    protected override bool CanReuseTransitionCapture(Widget other) => false;

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(TextWidget previous, TextWidget next) =>
        previous.Value == next.Value && ReferenceEquals(previous.LayoutProvider, next.LayoutProvider)
            ? UiInvalidation.None : UiInvalidation.Layout | UiInvalidation.Visual;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        if (_layout is null || _maximumWidth != constraints.MaxWidth || _value != Widget.Value
            || !ReferenceEquals(_provider, Widget.LayoutProvider))
        {
            ITextLayoutProvider provider = Widget.LayoutProvider
                ?? throw new InvalidOperationException("Text requires an explicit layout provider.");
            TextLayout2D layout = provider.CreateLayout(Widget.Value, constraints.MaxWidth)
                ?? throw new InvalidOperationException("The text provider returned no snapshot.");
            _layout = layout;
            _provider = provider;
            _value = Widget.Value;
            _maximumWidth = constraints.MaxWidth;
            InvalidateDrawing();
        }

        return constraints.Constrain(new(_layout.AdvanceBounds.Width, _layout.AdvanceBounds.Height));
    }

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        if (_layout is not null && ContentSize.X > 0 && ContentSize.Y > 0)
        {
            canvas.DrawText(_layout);
        }
    }
}
