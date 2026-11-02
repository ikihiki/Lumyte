using System.Numerics;
using Lumyte.Signals;
using WidgetDefinition = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Owns function-local Use values and the current rendered subtree.</summary>
public class UiFunctionElement : UiElement<UiFunctionWidget>
{
    private Dictionary<UiFunctionStateKey, object> _state = [];

    /// <summary>Initializes a new instance of the <see cref="UiFunctionElement"/> class.</summary>
    /// <param name="widget">The passive function parameters.</param>
    protected internal UiFunctionElement(UiFunctionWidget widget)
        : base(widget)
    {
    }

    /// <summary>Gets the current mounted result of the construction function.</summary>
    public UiElement Content => Children[0];

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => true;

    /// <inheritdoc/>
    protected override bool CanReuseTransitionCapture(WidgetDefinition other) => true;

    /// <inheritdoc/>
    protected override UiElementContent? BuildContent(WidgetDefinition next, UiElementBuildContext context)
    {
        var frame = new UiFunctionBuild(_state, context.Environment);
        WidgetDefinition body = frame.Run(((UiFunctionWidget)next).Evaluate, out SignalReadSet reads);
        return new(body, () => _state = frame.Values) { Reads = reads.IsEmpty ? null : reads };
    }

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(UiFunctionWidget previous, UiFunctionWidget next) => UiInvalidation.None;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = LayoutChild(Content, constraints);
        SetChildOffset(Content, Vector2.Zero);
        return size;
    }

    /// <inheritdoc/>
    protected override void OnUnmounted() => _state.Clear();
}
