using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Captures placement values while sharing retained surface content.</summary>
internal sealed partial class UiHostedDrawingSource
{
    private ulong? _retainedGeneration;

    internal DrawingSnapshot2D RecordRetainedFrame(ulong graphicsGeneration)
    {
        BeginFrame();
        try
        {
            if (_retainedGeneration != graphicsGeneration)
            {
                foreach (Presentation presentation in _presentations)
                {
                    presentation.Drawing?.Dispose();
                    presentation.Drawing = null;
                }

                _retainedGeneration = graphicsGeneration;
            }

            FrameItem[] items = PrepareFrame();
            var entries = new List<DrawingSceneEntry2D>();
            foreach (FrameItem item in items)
            {
                Presentation presentation = item.Presentation;
                UiWindowPresentationView view = item.View;
                _recorded.Add(presentation, view);
                if (!view.IsVisible)
                {
                    continue;
                }

                (presentation.Surface.VisualHost as UiVisualCoordinator)?.BeginRecord();
                presentation.Drawing ??= presentation.Surface.CreateDrawingScene();
                presentation.Drawing.UpdatePrepared();
                using DrawingSceneReadLease2D source = presentation.Drawing.Scene.Borrow();
                var scale = Matrix3x2.CreateScale(view.Scale);
                Matrix3x2 placement = Matrix3x2.CreateTranslation(view.BoundsInPresentation.X, view.BoundsInPresentation.Y) * scale;
                if (presentation.DrawingClip != view.ClipInPresentation)
                {
                    Rect2D clip = view.ClipInPresentation;
                    var path = new PathBuilder2D();
                    path.MoveTo(new(clip.X, clip.Y));
                    path.LineTo(new(clip.X + clip.Width, clip.Y));
                    path.LineTo(new(clip.X + clip.Width, clip.Y + clip.Height));
                    path.LineTo(new(clip.X, clip.Y + clip.Height));
                    path.Close();
                    presentation.DrawingClipPath = path.Build();
                    presentation.DrawingClip = clip;
                }

                foreach (DrawingSceneEntry2D entry in ((IDrawingSceneSource2D)source).Entries)
                {
                    DrawingSceneClip2D[] clips = [new(presentation.DrawingClipPath!, FillRule2D.NonZero, scale),
                        .. entry.Clips.Select(clip => clip with { Transform = clip.Transform * placement })];
                    entries.Add(entry with { Transform = entry.Transform * placement, Clips = clips });
                }

                CapturePresentation(presentation);
            }

            // No mutable scene or UI reference crosses the asynchronous driver boundary.
            // Only placement metadata is copied; commands and normalized pages remain shared.
            return new DrawingSnapshot2D(0, entries.ToArray());
        }
        catch
        {
            CompleteFrame(false);
            throw;
        }
    }
}
