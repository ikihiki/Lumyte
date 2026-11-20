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

    internal bool PassiveRow { get; set; }

    internal Func<bool>? Expanded { get; set; }

    internal Action<bool>? Expand { get; set; }

    internal UiTreeItemState? TreeItem { get; set; }

    internal float GlyphStart { get; set; }

    internal ItemNavigation? HierarchyNavigate { get; set; }

    internal UiOrientation Orientation { get; init; }

    internal required Func<bool> Selected { get; init; }

    internal required Func<UiAutomationId> SelectionOwner { get; set; }

    internal required Func<CollectionItemElement, bool> TabStop { get; init; }

    internal required Action<CollectionItemElement> Activate { get; init; }

    internal required ItemNavigation Navigate { get; init; }

    protected override UiElement CreateElement() => new CollectionItemElement(this);
}
