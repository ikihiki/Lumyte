using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>A borrowed owner-loop service; Controls never creates windows.</summary>
public interface IMenuPopupHost
{
    /// <summary>Queues one popup, running the content factory once on the owner loop.</summary>
    /// <param name="anchor">The live actual anchor, also identifying the logical parent surface.</param>
    /// <param name="size">Positive finite dimensions.</param>
    /// <param name="createContent">A fresh independent content factory.</param>
    /// <param name="passive">Whether input and editor focus must remain untouched.</param>
    /// <param name="closed">One terminal owner-loop notification, including failed opening.</param>
    /// <returns>An idempotent queued-close token; the host itself remains borrowed.</returns>
    IDisposable Open(UiElement anchor, Vector2 size, Func<Widget> createContent, bool passive, Action closed);

    /// <summary>Queues ItemInvoked dismissal for the chain containing this actual anchor.</summary>
    /// <param name="anchor">A command on the actual registered popup surface.</param>
    void ItemInvoked(UiElement anchor);
}
