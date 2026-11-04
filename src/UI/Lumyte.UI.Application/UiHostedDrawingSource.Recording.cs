using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Validates ordered presentations before retained capture.</summary>
internal sealed partial class UiHostedDrawingSource
{
    private FrameItem[] PrepareFrame()
    {
        // A later validator can mutate an earlier surface, including applying that change immediately.
        for (int attempt = 0; attempt < 100; attempt++)
        {
            Presentation[] presentations = _presentations.Where(p => !p.Closing && p.Active)
                .OrderBy(p => UiWindowStacking.Key(p.View.Role, p.View.Window, p.View.StackingOrder)).ToArray();
            var items = new FrameItem[presentations.Length];
            for (int index = 0; index < presentations.Length; index++)
            {
                Presentation presentation = presentations[index];
                if (!_paused.Contains(presentation.Surface) && presentation.View.IsVisible)
                {
                    PrepareSurface(presentation.Surface);
                }

                items[index] = CaptureItem(presentation);
            }

            bool stable = _presentations.Count(p => !p.Closing && p.Active) == presentations.Length;
            foreach (FrameItem item in items)
            {
                stable &= !item.Presentation.Closing && item.Presentation.Active && CaptureItem(item.Presentation) == item
                    && (!item.View.IsVisible || !item.Presentation.Surface.HasPendingFrameUpdates);
            }

            if (stable)
            {
                return items;
            }
        }

        throw new InvalidOperationException("Presentation validation did not converge.");
    }

    private FrameItem CaptureItem(Presentation presentation)
    {
        UiSurface surface = presentation.Surface;
        UiWindowPresentationView view = _paused.Contains(surface) ? presentation.View with { IsVisible = false } : presentation.View;
        return new(presentation, view, surface.TreeRevision, surface.VisualRevision, surface.InputVersionForWindowing, surface.SignalRevision);
    }

    private readonly record struct FrameItem(Presentation Presentation, UiWindowPresentationView View, ulong TreeRevision, ulong VisualRevision, object? InputVersion, UiSignalRevision Signals);
}
