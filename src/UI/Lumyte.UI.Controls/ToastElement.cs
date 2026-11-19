using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Composes notification sections with one ordinary, separately focusable dismissal button.</summary>
public class ToastElement : UiElement<Compose.Definitions.Toast>
{
    private object? _generation;
    private Widget? _dismiss;

    /// <summary>Initializes a new instance of the <see cref="ToastElement"/> class.</summary>
    /// <param name="widget">The passive notification declaration.</param>
    protected internal ToastElement(Compose.Definitions.Toast widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context)
    {
        var declaration = (Compose.Definitions.Toast)next;
        ArgumentException.ThrowIfNullOrWhiteSpace(declaration.DismissName);
        object generation = new();
        Lumyte.UI.Compose.Definitions.Button dismiss = Lumyte.UI.Compose.Button(width: 24, height: 24, automationName: declaration.DismissName, onClick: () =>
        {
            if (IsMounted && ReferenceEquals(_generation, generation))
            {
                Widget.OnDismissRequested?.Invoke();
            }
        });
        dismiss.Children.Initialize(declaration.DismissChildren.ToArray());
        Compose.Definitions.DialogContent actions = Compose.DialogContent();
        actions.ContentChildren.Initialize(declaration.ActionsChildren.ToArray());
        actions.ActionsChildren.Initialize(dismiss);
        Compose.Definitions.DialogContent sections = Compose.DialogContent();
        sections.HeaderChildren.Initialize(Passive(declaration.HeaderChildren));
        sections.ContentChildren.Initialize(Passive(declaration.ContentChildren));
        sections.ActionsChildren.Initialize(actions);
        return new(sections, () =>
        {
            _generation = generation;
            _dismiss = dismiss;
        });
    }

    /// <inheritdoc/>
    protected override bool AcceptsPointerTarget(UiElement target)
    {
        for (UiElement? node = target; node is not null && node != this; node = node.Parent)
        {
            if (ReferenceEquals(node.Widget, _dismiss) || Widget.ActionsChildren.Contains(node.Widget))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    protected override bool HitTestSelf(Vector2 localPosition) => false;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        UiElement content = Children[0];
        Vector2 size = LayoutChild(content, constraints);
        SetChildOffset(content, Vector2.Zero);
        return constraints.Constrain(size);
    }

    private static Widget Passive(WidgetChildren content)
    {
        Compose.Definitions.DialogContent root = Compose.DialogContent(hitTestVisible: false);
        root.ContentChildren.Initialize(content.ToArray());
        return root;
    }
}
