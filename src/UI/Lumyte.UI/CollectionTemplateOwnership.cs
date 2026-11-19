using System.Runtime.CompilerServices;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

[assembly: InternalsVisibleTo("Lumyte.UI.Collections")]

namespace Lumyte.UI;

internal static class CollectionTemplateOwnership
{
    internal static void ValidateFreshItemRoot(this Widget widget)
    {
        if (widget.HasDeclarationParent || widget.MountedInstanceCount != 0)
        {
            throw new InvalidOperationException("Item templates must return new, unparented and unmounted roots.");
        }
    }

    internal static UiElementContent WithItemOwnership(this UiElementContent content, params Widget[] roots) =>
        new(content.Content, content.Commit) { Reads = content.Reads, FreshRealizations = content.FreshRealizations, PreparationState = content.PreparationState, PrepareLayout = content.PrepareLayout, CollectionItemRoots = Array.AsReadOnly((Widget[])roots.Clone()) };

    internal static UiElementContent WithFreshItemOwnership(this UiElementContent content, params Widget[] roots) =>
        new(content.Content, content.Commit)
        {
            Reads = content.Reads,
            CollectionItemRoots = Array.AsReadOnly((Widget[])roots.Clone()),
            RequiresFreshDescendants = true,
            FreshRealizations = content.FreshRealizations,
            PreparationState = content.PreparationState,
            PrepareLayout = content.PrepareLayout,
        };

    internal static IEnumerable<Widget> GetDeclaredItemChildren(this Widget widget) => widget.ChildCollections.SelectMany(slot => slot);
}
