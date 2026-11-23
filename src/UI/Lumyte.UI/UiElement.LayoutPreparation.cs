using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Provides a scoped layout view over unpublished proposals without mounting them.</summary>
public partial class UiElement
{
    private UiSurface? _layoutPreparationSurface;
    private Widget? _layoutPreparationWidget;
    private UiElement? _layoutPreparationParent;
    private UiElementChildren[]? _layoutPreparationSlots;
    private IReadOnlyList<UiElement>? _layoutPreparationChildren;

    internal Widget PreparationWidget => _layoutPreparationWidget ?? _widget;

    internal UiSurface? PreparationSurface => _layoutPreparationSurface ?? Surface;

    internal IReadOnlyList<UiElement> PreparationChildren => _layoutPreparationChildren ?? Children;

    private bool UsesPreparedTopology => _layoutPreparationSurface?.LayoutPass is not null;

    internal void SetLayoutPreparation(UiSurface? surface, Widget? widget = null, UiElement? parent = null, IReadOnlyList<UiElement>[]? slots = null)
    {
        _layoutPreparationSurface = surface;
        _layoutPreparationWidget = widget;
        _layoutPreparationParent = parent;
        _layoutPreparationSlots = slots?.Select((children, index) =>
        {
            var slot = new UiElementChildren(this, index);
            slot.Publish(children);
            return slot;
        }).ToArray();
        _layoutPreparationChildren = slots?.SelectMany(slot => slot).ToArray();
    }
}
