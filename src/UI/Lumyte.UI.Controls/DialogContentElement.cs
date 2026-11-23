using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Builds an optional ordinary close button that routes to the enclosing presentation owner.</summary>
public class DialogContentElement : ContentSectionsElement
{
    /// <summary>Initializes a new instance of the <see cref="DialogContentElement"/> class.</summary>
    /// <param name="widget">The passive content declaration.</param>
    protected internal DialogContentElement(Compose.Definitions.DialogContent widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context)
    {
        var declaration = (Compose.Definitions.DialogContent)next;
        if (declaration.CloseChildren.Count == 0)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(declaration.CloseName);
        Lumyte.UI.Compose.Definitions.Button close = Lumyte.UI.Compose.Button(
            width: 24,
            height: 24,
            automationName: declaration.CloseName,
            onClick: () =>
            {
                if (IsMounted)
                {
                    Surface?.RequestClose();
                }
            });
        close.Children.Initialize(declaration.CloseChildren.ToArray());
        Compose.Definitions.DialogContent actions = Compose.DialogContent();
        actions.ContentChildren.Initialize(declaration.ActionsChildren.ToArray());
        actions.ActionsChildren.Initialize(close);
        Compose.Definitions.DialogContent sections = Compose.DialogContent();
        sections.HeaderChildren.Initialize(declaration.HeaderChildren.ToArray());
        sections.ContentChildren.Initialize(declaration.ContentChildren.ToArray());
        sections.ActionsChildren.Initialize(actions);
        return new(sections);
    }
}
