using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>A passive parameter declaration for a generated construction function.</summary>
public abstract class UiFunctionWidget : Widget
{
    /// <summary>Initializes a new instance of the <see cref="UiFunctionWidget"/> class.</summary>
    protected UiFunctionWidget()
    {
    }

    /// <inheritdoc/>
    protected override bool RebuildsChildren => true;

    internal Widget Evaluate() => Build();

    /// <summary>Evaluates the user function with this declaration's concrete parameter values.</summary>
    /// <returns>A fresh parentless UI declaration.</returns>
    protected abstract Widget Build();

    /// <inheritdoc/>
    protected override UiElement CreateElement() => new UiFunctionElement(this);
}
