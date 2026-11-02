using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Windowing;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.Application;

internal static class UiHostDrawing
{
    internal static (Vector2 Origin, Rect2D Clip) Geometry(WindowNode node)
    {
        var path = new Stack<WindowNode>();
        for (WindowNode current = node; current != node.PresentationRoot; current = current.Parent ?? throw new InvalidOperationException("A hosted node lost its presentation root."))
        {
            path.Push(current);
        }

        Vector2 size = node.PresentationRoot.ClientSize;
        Vector2 origin = default;
        var clip = new Rect2D(0, 0, size.X, size.Y);
        while (path.TryPop(out WindowNode? current))
        {
            origin += new Vector2(current.Bounds.X, current.Bounds.Y);
            float left = Math.Max(clip.X, origin.X);
            float top = Math.Max(clip.Y, origin.Y);
            float right = Math.Min(clip.X + clip.Width, origin.X + current.ClientSize.X);
            float bottom = Math.Min(clip.Y + clip.Height, origin.Y + current.ClientSize.Y);
            clip = new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }

        return (origin, clip);
    }

    internal static bool Drawable(WindowState state) => state.IsVisible && state.Lifetime == WindowLifetime.Open && state.PixelSize.Width > 0 && state.PixelSize.Height > 0;
}
