using System.Numerics;

namespace Lumyte.Maps;

/// <summary>Updates the camera and computes demand without resource acquisition.</summary>
public sealed partial class MapSession
{
    /// <summary>Pans by finite logical screen pixels; positive displacement moves the map with the pointer.</summary>
    /// <param name="displacement">Logical pointer displacement.</param>
    public void Pan(Vector2 displacement)
    {
        ValidatePoint(displacement);
        (double x, double y) = Camera.Project();
        double world = 512 * Math.Pow(2, Camera.Zoom);
        SetCamera(Unproject(x - (displacement.X / world), y - (displacement.Y / world), Camera.Zoom));
    }

    /// <summary>Changes zoom while preserving the geographic point beneath the local pointer, except at latitude limits.</summary>
    /// <param name="position">Finite viewport-local logical pointer position.</param>
    /// <param name="zoomDelta">Finite continuous zoom increment; clamped to the manifest range.</param>
    public void ZoomAt(Vector2 position, double zoomDelta)
    {
        ValidatePoint(position);
        if (!double.IsFinite(zoomDelta))
        {
            throw new ArgumentOutOfRangeException(nameof(zoomDelta));
        }

        double zoom = Math.Clamp(Camera.Zoom + zoomDelta, _tiles.MinZoom, _tiles.MaxZoom);
        (double x, double y) = Camera.Project();
        double before = 512 * Math.Pow(2, Camera.Zoom);
        double after = 512 * Math.Pow(2, zoom);
        double dx = position.X - (_viewport.X / 2.0);
        double dy = position.Y - (_viewport.Y / 2.0);
        SetCamera(Unproject(x + (dx / before) - (dx / after), y + (dy / before) - (dy / after), zoom));
    }

    private static void ValidatePoint(Vector2 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private static MapCamera Unproject(double x, double y, double zoom) =>
        new((x * 360) - 180, Math.Atan(Math.Sinh(Math.PI * (1 - (2 * Math.Clamp(y, 0, 1))))) * 180 / Math.PI, zoom);

    private bool IsVisible(MapTileId id)
    {
        if (_viewport.X <= 0 || _viewport.Y <= 0 || (_options.SelectVisibleTiles && id.Zoom != SourceZoom))
        {
            return false;
        }

        (double x, double y) = Camera.Project();
        double world = 512 * Math.Pow(2, Camera.Zoom);
        double count = 1 << id.Zoom;
        double halfWidth = _viewport.X / (2 * world);
        double halfHeight = _viewport.Y / (2 * world);
        if ((id.Y / count) >= y + halfHeight || ((id.Y + 1) / count) <= y - halfHeight)
        {
            return false;
        }

        double left = id.X / count;
        double right = (id.X + 1) / count;
        double first = Math.Floor(x - halfWidth - right) + 1;
        double last = Math.Ceiling(x + halfWidth - left) - 1;
        return first <= last;
    }
}
