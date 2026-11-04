using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

/// <summary>Retains only resource-free builtin drawings while renewing presentation receipts each frame.</summary>
internal sealed partial class UiHostedDrawingSource
{
    private DrawList2D? _cachedCommands;
    private FrameItem[] _cachedItems = [];
    private ulong _cachedGraphicsGeneration;
    private UiWindowSourceSnapshot? _cachedState;

    internal DrawList2D RecordFrame(ulong graphicsGeneration)
    {
        BeginFrame();
        try
        {
            FrameItem[] items = PrepareFrame();
            UiWindowSourceSnapshot state = CaptureState();
            bool reusable = items.All(item => !item.View.IsVisible || CanCache(item.Presentation));
            if (reusable && _cachedCommands is { } cached && _cachedGraphicsGeneration == graphicsGeneration
                && _cachedState == state && items.AsSpan().SequenceEqual(_cachedItems))
            {
                foreach (FrameItem item in items)
                {
                    _recorded.Add(item.Presentation, item.View);
                    if (item.View.IsVisible)
                    {
                        (item.Presentation.Surface.VisualHost as UiVisualCoordinator)?.BeginRecord();
                        CapturePresentation(item.Presentation);
                    }
                }

                return cached;
            }

            DiscardCache();
            var canvas = new Canvas2D();
            canvas.Begin();
            RecordItems(canvas, items);
            DrawList2D result = canvas.End();
            if (reusable)
            {
                _cachedCommands = result;
                _cachedItems = items;
                _cachedGraphicsGeneration = graphicsGeneration;
                _cachedState = state;
            }

            return result;
        }
        catch
        {
            DiscardCache();
            CompleteFrame(false);
            throw;
        }
    }

    private static bool CanCache(Presentation presentation)
    {
        UiSurface surface = presentation.Surface;
        if (surface.HasAnimationRegistrations || surface.VisualEntries.Count != 0 || surface.PageTransitioning
            || (surface.VisualHost is not null && surface.VisualHost is not UiVisualCoordinator { Page: null }))
        {
            return false;
        }

        if (presentation.EligibilityRevision != surface.StructureRevision)
        {
            presentation.EligibilityRevision = surface.StructureRevision;
            presentation.CacheEligible = IsBuiltinTree(surface.RootElement);
        }

        return presentation.CacheEligible;
    }

    private static bool IsBuiltinTree(UiElement root)
    {
        var pending = new Stack<UiElement>();
        pending.Push(root);
        while (pending.TryPop(out UiElement? element))
        {
            Type type = element.Widget.GetType();
            if (type != typeof(Lumyte.UI.Compose.Definitions.Panel) && type != typeof(Lumyte.UI.Compose.Definitions.Stack)
                && type != typeof(Lumyte.UI.Compose.Definitions.Rectangle))
            {
                return false;
            }

            for (int index = 0; index < element.Children.Count; index++)
            {
                pending.Push(element.Children[index]);
            }
        }

        return true;
    }

    private void DiscardCache()
    {
        _cachedCommands = null;
        _cachedItems = [];
        _cachedState = null;
    }

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
