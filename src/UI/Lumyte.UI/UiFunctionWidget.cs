using System.Numerics;
using Lumyte.Signals;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Hosts a generated construction function and its identity-preserved local signal values.</summary>
/// <remarks>The UI invokes Build after matching the previous declaration. Applications use the generated static factory.</remarks>
public abstract class UiFunctionWidget : Widget
{
    private readonly WidgetChildren _content;
    private Dictionary<UiFunctionStateKey, object> _state = [];

    /// <summary>Initializes a new instance of the <see cref="UiFunctionWidget"/> class.</summary>
    protected UiFunctionWidget()
    {
        _content = CreateChildren(1);
    }

    /// <summary>Gets the returned tree after the surface has evaluated the function.</summary>
    public Widget Content => _content.Count != 0 ? _content[0] : throw new InvalidOperationException("The function has not been evaluated by a surface.");

    /// <inheritdoc/>
    protected override bool SupportsLocalDeclarationValidation => CompositionFactory is not null;

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => true;

    internal void BuildContent(UiFunctionWidget? previous)
    {
        if (_content.IsInitialized)
        {
            return;
        }

        var frame = new UiFunctionBuild(previous?._state, BuildContexts);
        Widget body = frame.Run(Build, out SignalReadSet reads);
        _content.Initialize(body);
        _state = frame.Values;
        if (!reads.IsEmpty)
        {
            AddSignalArgument(this, () =>
            {
                reads.Read();
                return !reads.IsCurrent;
            });
        }
    }

    /// <summary>Evaluates the user function with this declaration's concrete parameter values.</summary>
    /// <returns>A fresh disconnected, parentless UI tree.</returns>
    protected abstract Widget Build();

    /// <inheritdoc/>
    protected override bool CanReuseTransitionCapture(Widget previous) => true;

    /// <inheritdoc/>
    protected override bool CanReuseInput(Widget previous) => true;

    /// <inheritdoc/>
    protected override bool CanReuseLayout(Widget previous) => true;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = LayoutChild(Content, constraints);
        SetChildOffset(Content, Vector2.Zero);
        return size;
    }
}
