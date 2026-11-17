using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

internal delegate UiInputResult ItemNavigation(CollectionItemElement item, in UiInputContext context);

internal sealed class CollectionItem : Widget
{
    internal CollectionItem()
    {
        Role = UiRole.ListItem;
        Focusable = true;
        Content = CreateChildren(1);
    }

    internal WidgetChildren Content { get; }

    internal UiOrientation Orientation { get; init; }

    internal required Func<bool> Selected { get; init; }

    internal required Func<UiAutomationId> SelectionOwner { get; init; }

    internal required Func<CollectionItemElement, bool> TabStop { get; init; }

    internal required Action<CollectionItemElement> Activate { get; init; }

    internal required ItemNavigation Navigate { get; init; }

    protected override UiElement CreateElement() => new CollectionItemElement(this);
}
