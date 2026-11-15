using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Geometry;
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
            Bounds2D<float> bounds = Bounds2D<float>.FromOriginSize(clip.X, clip.Y, clip.Width, clip.Height).Intersect(Bounds2D<float>.FromOriginSize(origin.X, origin.Y, current.ClientSize.X, current.ClientSize.Y));
            clip = new(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        }

        return (origin, clip);
    }

    internal static bool Drawable(WindowState state) => state.IsVisible && state.Lifetime == WindowLifetime.Open && state.PixelSize.Width > 0 && state.PixelSize.Height > 0;
}
