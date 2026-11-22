using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls.Windowing;

/// <summary>Provides recipes using existing menu declarations and popup ownership.</summary>
public static class MenuCompose
{
    /// <summary>Creates a normal MenuItem with a fresh submenu factory; its parent Menu supplies the borrowed popup host.</summary>
    /// <param name="key">The nonempty ordinal item key.</param>
    /// <param name="createContent">Creates a fresh Menu root once per accepted opening.</param>
    /// <param name="isEnabled">Whether the item can be selected.</param>
    /// <param name="accessibleName">An explicit semantic name, not inferred from a visual label.</param>
    /// <param name="popupSize">Positive finite popup dimensions.</param>
    /// <returns>An ordinary MenuItem; add an optional ordinary visual label with its content indexer.</returns>
    public static Compose.Definitions.MenuItem Submenu(string key, Func<Widget> createContent, bool isEnabled = true, string? accessibleName = null, Vector2? popupSize = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(createContent);
        return Compose.MenuItem(key: key, createSubmenu: createContent, isEnabled: isEnabled, automationName: accessibleName, popupSize: popupSize ?? new(240, 160));
    }
}
