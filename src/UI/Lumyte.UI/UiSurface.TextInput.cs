using System.Numerics;

namespace Lumyte.UI;

/// <summary>Defines ui surface.</summary>
public sealed partial class UiSurface
{
    /// <summary>Finds an attached widget by its current surface-local identity.</summary>
    /// <param name="id">The attachment identity.</param>
    /// <returns>The borrowed widget, or null after detachment.</returns>
    public UiElement? FindElement(UiAutomationId id)
    {
        ThrowIfDisposed();
        return id.SurfaceId == _rootElement.AutomationId.SurfaceId ? CollectElements(_rootElement).FirstOrDefault(node => node.AutomationId == id) : null;
    }

    /// <summary>Hit Test.</summary>
    /// <param name = "logicalPosition">The logical position.</param>
    /// <returns>The operation result.</returns>
    public UiElement? HitTest(Vector2 logicalPosition)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(logicalPosition.X) || !float.IsFinite(logicalPosition.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(logicalPosition));
        }

        return UiInputGeometry.Contains(Vector2.Zero, _viewport, logicalPosition) ? _rootElement.FindInputHit(logicalPosition, _inputVersion) : null;
    }
}
