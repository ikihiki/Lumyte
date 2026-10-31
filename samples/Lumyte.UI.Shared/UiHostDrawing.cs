using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Windowing;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.UI.SampleSupport;

internal static class UiHostDrawing
{
    internal static void Record(DrawingCanvas2D canvas, IWindow window, UiSurface surface)
    {
        if (!Drawable(window.State))
        {
            return;
        }

        ValidateScale(window.State.Scale);
        surface.SetViewport(window.State.ClientSize);
        surface.UpdateLayout();
        canvas.Save();
        try
        {
            canvas.Scale(window.State.Scale);
            canvas.ClipRect(new(0, 0, window.State.ClientSize.X, window.State.ClientSize.Y));
            surface.Record(canvas);
        }
        finally
        {
            canvas.Restore();
        }
    }

    internal static void Record(DrawingCanvas2D canvas, WindowNode node, UiSurface surface)
    {
        if (!node.IsEffectivelyVisible || node.Lifetime != WindowLifetime.Open || !Drawable(node.PresentationRoot.NativeWindow.State))
        {
            return;
        }

        ValidateScale(node.Scale);
        surface.SetViewport(node.ClientSize);
        surface.UpdateLayout();
        (Vector2 origin, Rect2D clip) = Geometry(node);
        canvas.Save();
        try
        {
            canvas.Scale(node.Scale);
            canvas.ClipRect(clip);
            canvas.Translate(origin);
            surface.Record(canvas);
        }
        finally
        {
            canvas.Restore();
        }
    }

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

    private static void ValidateScale(Vector2 scale)
    {
        if (!float.IsFinite(scale.X) || !float.IsFinite(scale.Y) || scale.X <= 0 || scale.Y <= 0)
        {
            throw new InvalidOperationException("The observed window scale must be finite and positive.");
        }
    }
}
