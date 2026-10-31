using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private sealed class RebuildCandidate(Widget root)
    {
        internal Widget Root { get; } = root;
    }
}
