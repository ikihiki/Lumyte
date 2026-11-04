using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;

namespace Lumyte.Graphics.Effects.Benchmarks;

internal sealed record EffectProfile(string Id, string Kind, int Layers, int Sigma, bool FullBackdrop, int Width = 1920, int Height = 1080)
{
    internal static IReadOnlyList<EffectProfile> All { get; } =
    [
        new("opaque-1080p", "opaque", 0, 0, false),
        new("isolation-1-1080p", "isolation", 1, 0, false),
        new("isolation-4-1080p", "isolation", 4, 0, false),
        new("blur-1-s2-1080p", "blur", 1, 2, false),
        new("blur-1-s6-1080p", "blur", 1, 6, false),
        new("blur-4-s2-1080p", "blur", 4, 2, false),
        new("backdrop-quarter-s2-1080p", "backdrop", 0, 2, false),
        new("backdrop-full-s2-1080p", "backdrop", 0, 2, true),
        new("blur-1-s2-720p", "blur", 1, 2, false, 1280, 720),
    ];

    internal Rect2D BackdropBounds => FullBackdrop ? new(0, 0, Width, Height) : new(Width / 4, Height / 4, Width / 2, Height / 2);

    internal Rect2D Content(int layer)
    {
        int columns = Layers == 4 ? 2 : 1;
        int tileWidth = Width / columns;
        int tileHeight = Height / columns;
        return new(((layer % columns) * tileWidth) + (tileWidth / 4), ((layer / columns) * tileHeight) + (tileHeight / 4), tileWidth / 2, tileHeight / 2);
    }

    internal RenderGraphDrawList2D Record()
    {
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        if (Kind is "backdrop" or "opaque")
        {
            canvas.FillRect(new Rect2D(0, 0, Width / 2, Height), new Color2D(1, 1, 1, 1));
            canvas.FillRect(new Rect2D(Width / 2, 0, Width / 2, Height), new Color2D(1, 0, 0, 1));
            if (Kind == "backdrop")
            {
                canvas.DrawBackdropEffect(new BackdropEffectOptions2D
                {
                    OutputBounds = BackdropBounds,
                    Effects = scope => scope.AppendGaussianBlur(scope.Source, Sigma, Sigma),
                });
            }
        }
        else
        {
            for (int layer = 0; layer < Layers; layer++)
            {
                Rect2D bounds = Content(layer);
                canvas.BeginLayer(new LayerOptions2D
                {
                    ContentBounds = new Rect2D(0, 0, Width, Height),
                    Opacity = Kind == "isolation" ? 0.5f : 1,
                    Effects = Kind == "blur" ? scope => scope.AppendGaussianBlur(scope.Source, Sigma, Sigma) : null,
                });
                canvas.FillRect(bounds, new Color2D(1, 1, 1, 1));
                if (Kind == "isolation")
                {
                    // Overlap intentionally tests group opacity versus per-command alpha.
                    canvas.FillRect(new Rect2D(bounds.X + (bounds.Width / 2), bounds.Y, bounds.Width / 2, bounds.Height), new Color2D(1, 0, 0, 1));
                }

                canvas.EndLayer();
            }
        }

        return canvas.End();
    }
}
