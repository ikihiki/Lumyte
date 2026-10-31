using System.Numerics;

namespace Lumyte.UI.TextEditing;

/// <summary>Defines ui text hit testing.</summary>
public static class UiTextHitTesting
{
    /// <summary>Resolve Editor.</summary>
    /// <param name = "surface">The surface.</param>
    /// <param name = "logicalPosition">The logical position.</param>
    /// <returns>The operation result.</returns>
    public static Compose.Definitions.TextBox? ResolveEditor(UiSurface surface, Vector2 logicalPosition)
    {
        ArgumentNullException.ThrowIfNull(surface);
        if (surface.ProbePointer(logicalPosition).ActivationTarget is not null)
        {
            return null;
        }

        for (Lumyte.UI.Compose.Definitions.Widget? hit = surface.HitTest(logicalPosition); hit is not null; hit = hit.Parent)
        {
            if (hit is Compose.Definitions.TextBox editor)
            {
                return editor;
            }
        }

        return null;
    }
}
