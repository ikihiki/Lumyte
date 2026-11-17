using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Borrowed popup admission using the host's ordinary window and input scope.</summary>
public interface ICollectionPopupHost
{
    /// <summary>Queues a popup with independently constructed content.</summary>
    /// <param name="anchor">The mounted selector owner.</param>
    /// <param name="size">The finite popup viewport.</param>
    /// <param name="createContent">Fresh content constructed once per opening.</param>
    /// <param name="closed">Called after popup cleanup.</param>
    /// <returns>An idempotent close request.</returns>
    IDisposable Open(UiElement anchor, Vector2 size, Func<Widget> createContent, Action closed);
}
