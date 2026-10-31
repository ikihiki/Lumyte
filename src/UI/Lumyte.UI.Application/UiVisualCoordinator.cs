using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Windowing;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application;

internal sealed class UiVisualCoordinator(UiApplication application, UiSurface surface, Func<UiWindowId?> window) : IUiVisualHost, IAsyncDisposable
{
    private readonly Dictionary<UiPresenceEntry, Element> _elements = [];
    private readonly List<Task> _retiring = [];
    private readonly HashSet<UiVisualSnapshot> _recorded = [];
    private readonly HashSet<UiVisualSnapshot> _captureImages = [];
    private bool _capturing;
    private bool _published;
    private bool _everVisible;
    private bool _stopping;
    private Task? _close;
    private bool _retirementErrorReported;

    public bool CanPresent => _published;

    public UiMotionOptions Motion => application.Motion;

    internal ulong PresentedSequence { get; private set; }

    internal ulong PresentedVisualRevision { get; private set; }

    internal UiSurface Surface => surface;

    internal string CaptureFailureCode { get; private set; } = "capture-unavailable";

    internal UiPageVisualFlight? Page { get; set; }

    internal IReadOnlyCollection<UiVisualSnapshot> Recorded => _recorded;

    public bool Record(Widget widget, DrawingCanvas2D canvas)
    {
        if (ReferenceEquals(widget, surface.Root) && Page is { Committed: true, IsFinal: false } page)
        {
            page.Record(canvas, this);
            return true;
        }

        if (widget.Presence is not { IsActive: true } entry)
        {
            return false;
        }

        if (_elements.TryGetValue(entry, out Element? element))
        {
            element.Layer.Draw(canvas, new(0, 0, widget.Size.X, widget.Size.Y), entry.Pose);
            return true;
        }

        return entry.State != UiPresenceState.Exiting || entry.Pose != UiVisualPose.Identity;
    }

    public ValueTask DisposeAsync() => new(_close ??= CloseAsync());

    internal static Rect2D Bounds(Widget widget)
    {
        Vector2 origin = Vector2.Zero;
        for (Widget? node = widget; node is not null; node = node.Parent)
        {
            origin += new Vector2(node.Bounds.X, node.Bounds.Y);
        }

        return new(origin.X, origin.Y, widget.Size.X, widget.Size.Y);
    }

    internal void SuspendPublication() => _published = false;

    internal void Publish() => _published = true;

    internal void BeginRecord() => _recorded.Clear();

    internal void Presented(UiWindowPresentationView view, bool ordinary, ulong visualRevision)
    {
        PresentedSequence++;
        PresentedVisualRevision = visualRevision;
        UiWindowSnapshot? state = CaptureWindow();
        if (ordinary && state is not null && state.Scale == view.Scale && state.BoundsInPresentation == view.BoundsInPresentation && state.ClipInPresentation == view.ClipInPresentation)
        {
            Page?.Presented();
        }
    }

    internal void Draw(DrawingCanvas2D canvas, UiVisualSnapshot image, Rect2D bounds, UiVisualPose pose)
    {
        if (pose.Opacity == 0)
        {
            return;
        }

        Track(image);
        canvas.Save();
        try
        {
            Vector2 center = new(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
            canvas.Translate(center + pose.Offset);
            canvas.Scale(pose.Scale);
            canvas.Translate(-center);
            var paint = PaintGraph2D.ImagePattern(new() { View = image.View, Source = new(0, 0, image.Width, image.Height), Alpha = AlphaRepresentation2D.Premultiplied, TileX = ImageTileMode2D.Decal, TileY = ImageTileMode2D.Decal, PatternToLocal = Matrix3x2.CreateScale(bounds.Width / image.Width, bounds.Height / image.Height) * Matrix3x2.CreateTranslation(bounds.X, bounds.Y) });
            canvas.FillRect(bounds, PaintGraph2D.Opacity(pose.Opacity, paint));
        }
        finally
        {
            canvas.Restore();
        }
    }

    internal void DrawConnected(DrawingCanvas2D canvas, UiVisualSnapshot previous, UiVisualSnapshot next, Rect2D bounds, float amount)
    {
        Track(previous);
        Track(next);
        PaintGraph2D Pattern(UiVisualSnapshot image) => PaintGraph2D.ImagePattern(new() { View = image.View, Source = new(0, 0, image.Width, image.Height), Alpha = AlphaRepresentation2D.Premultiplied, TileX = ImageTileMode2D.Decal, TileY = ImageTileMode2D.Decal, PatternToLocal = Matrix3x2.CreateScale(bounds.Width / image.Width, bounds.Height / image.Height) * Matrix3x2.CreateTranslation(bounds.X, bounds.Y) });
        var oldPaint = PaintGraph2D.Opacity(1 - amount, Pattern(previous));
        var newPaint = PaintGraph2D.Opacity(amount, Pattern(next));
        canvas.FillRect(bounds, PaintGraph2D.Composite(newPaint, oldPaint, CompositeMode2D.Plus));
    }

    internal UiWindowSnapshot? CaptureWindow() => window() is { } id && application.Windows.TryGetSnapshot(id, out UiWindowSnapshot? state) ? state : null;

    internal void TrackImage(UiVisualSnapshot image) => Track(image);

    internal (DrawList2D Drawing, UiVisualSnapshot[] Borrowed) RecordLayer(Widget widget, IReadOnlySet<Widget>? exclusions, bool includeAncestors)
    {
        bool previousCapturing = _capturing;
        UiVisualSnapshot[] previous = _captureImages.ToArray();
        _capturing = true;
        _captureImages.Clear();
        try
        {
            DrawList2D drawing = surface.CaptureVisual(widget, Vector2.One, exclusions, includeAncestors: includeAncestors, bounded: false);
            return (drawing, _captureImages.ToArray());
        }
        finally
        {
            _capturing = previousCapturing;
            _captureImages.Clear();
            foreach (UiVisualSnapshot image in previous)
            {
                _captureImages.Add(image);
            }
        }
    }

    internal Task<UiVisualSnapshot?> RasterizeLayerAsync(DrawList2D drawing, Vector2 size, IReadOnlyList<UiVisualSnapshot> borrowed, UiVisualSnapshot? cache)
    {
        CaptureFailureCode = "capture-unavailable";
        UiWindowSnapshot? state = CaptureWindow();
        if (_stopping || !application.Driver.SupportsVisuals || state is null)
        {
            return Task.FromResult<UiVisualSnapshot?>(null);
        }

        double width = Math.Ceiling(size.X * (double)state.Scale.X);
        double height = Math.Ceiling(size.Y * (double)state.Scale.Y);
        if (width < 1 || height < 1 || width > uint.MaxValue || height > uint.MaxValue)
        {
            return Task.FromResult<UiVisualSnapshot?>(null);
        }

        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Scale(state.Scale);
        canvas.Replay(drawing);
        DrawList2D physical = canvas.End();
        if (cache is not null && cache.Width == (uint)width && cache.Height == (uint)height)
        {
            return UpdateLayerAsync(cache, physical, borrowed);
        }

        IDisposable? reservation = application.VisualBudget.Reserve((uint)width, (uint)height);
        if (reservation is null)
        {
            CaptureFailureCode = "budget-exceeded";
            return Task.FromResult<UiVisualSnapshot?>(null);
        }

        return application.Driver.CaptureVisualAsync(physical, (uint)width, (uint)height, reservation, borrowed).AsTask();
    }

    internal Task<UiVisualSnapshot?> CaptureAsync(Widget widget, IReadOnlySet<Widget>? exclude = null, bool logical = false)
    {
        CaptureFailureCode = "capture-unavailable";
        UiWindowSnapshot? state = CaptureWindow();
        if (_stopping || !application.Driver.SupportsVisuals || (state is null || (!logical && !state.IsVisible)) || widget.Size.X <= 0 || widget.Size.Y <= 0
            || widget.CollectSubtree().Any(node => !node.TransitionCaptureSupported))
        {
            return Task.FromResult<UiVisualSnapshot?>(null);
        }

        double width = Math.Ceiling(widget.Size.X * (double)state.Scale.X);
        double height = Math.Ceiling(widget.Size.Y * (double)state.Scale.Y);
        if (width < 1 || height < 1 || width > uint.MaxValue || height > uint.MaxValue)
        {
            return Task.FromResult<UiVisualSnapshot?>(null);
        }

        IDisposable? reservation = application.VisualBudget.Reserve((uint)width, (uint)height);
        if (reservation is null)
        {
            CaptureFailureCode = "budget-exceeded";
            return Task.FromResult<UiVisualSnapshot?>(null);
        }

        try
        {
            _capturing = true;
            _captureImages.Clear();
            DrawList2D commands;
            try
            {
                commands = surface.CaptureVisual(widget, state.Scale, exclude, logical);
            }
            finally
            {
                _capturing = false;
            }

            return application.Driver.CaptureVisualAsync(commands, (uint)width, (uint)height, reservation, _captureImages.ToArray()).AsTask();
        }
        catch
        {
            reservation.Dispose();
            throw;
        }
    }

    internal void Advance()
    {
        if (_elements.Count == 0 && _retiring.Count == 0 && surface.VisualEntries.Count == 0 && (Page is null || Page.Completion.IsCompleted))
        {
            return;
        }

        foreach (Task task in _retiring.Where(task => task.IsCompleted).ToArray())
        {
            _retiring.Remove(task);
            task.GetAwaiter().GetResult();
        }

        UiWindowSnapshot? state = CaptureWindow();
        bool available = _published && !_stopping && application.State is UiApplicationState.Starting or UiApplicationState.Running && application.Driver.CanAnimate && state is { IsVisible: true, ClientSize.X: > 0, ClientSize.Y: > 0 };
        if (available)
        {
            _everVisible = true;
        }

        if (!_everVisible && !available && !_stopping)
        {
            Page?.CheckHiddenDeadline();
            return;
        }

        Page?.Advance(available, state);
        foreach ((UiPresenceEntry entry, Element element) in _elements.ToArray())
        {
            bool stale = !surface.VisualEntries.Contains(entry) || !entry.IsActive || !available || application.GraphicsGeneration != element.Epoch || state?.Scale != element.Scale;
            bool expired = application.TimeProvider.GetElapsedTime(element.Created) >= Motion.MaximumVisualLifetime
                || (!element.Layer.Ready && application.TimeProvider.GetElapsedTime(element.Created) >= Motion.PreparationTimeout);
            if (stale || expired)
            {
                if (entry.IsActive)
                {
                    entry.Skip();
                }

                Retire(entry, element);
                continue;
            }

            element.Layer.Advance();
            if (element.Layer.Failed)
            {
                entry.Skip();
                Retire(entry, element);
            }
            else if (element.Layer.Ready && entry.WantsCapture)
            {
                entry.Begin(application.AnimationClock.Now);
            }
        }

        if (!available)
        {
            surface.InterruptVisuals();
            return;
        }

        foreach (UiPresenceEntry entry in surface.VisualEntries.ToArray())
        {
            if (!entry.WantsCapture || _elements.ContainsKey(entry))
            {
                continue;
            }

            IDisposable? active = application.VisualBudget.BeginTransition();
            if (active is null || !application.Driver.SupportsVisuals)
            {
                active?.Dispose();
                entry.Skip();
                continue;
            }

            var layer = new UiVisualLayer(application, this, () => entry.Widget, entry.NeedsOpacityLayer);
            var element = new Element(active, application.TimeProvider.GetTimestamp(), layer, state!, application.GraphicsGeneration);
            _elements.Add(entry, element);
            layer.Advance();
            if (layer.Ready && entry.WantsCapture)
            {
                entry.Begin(application.AnimationClock.Now);
            }
        }
    }

    internal void BeginStop()
    {
        _stopping = true;
        Page?.Stop();
        surface.InterruptVisuals();
        foreach (UiPresenceEntry entry in surface.VisualEntries)
        {
            entry.Detach();
        }

        foreach ((UiPresenceEntry entry, Element element) in _elements.ToArray())
        {
            Retire(entry, element);
        }
    }

    internal async ValueTask DrainAsync()
    {
        BeginStop();
        await application.Driver.DrainVisualsAsync();
        try
        {
            await Task.WhenAll(_retiring.Where(task => !task.IsFaulted));
        }
        catch (Exception error)
        {
            if (!_retirementErrorReported)
            {
                _retirementErrorReported = true;
                application.FailInput(error);
            }
        }

        foreach (Task failed in _retiring.Where(task => task.IsFaulted))
        {
            if (!_retirementErrorReported)
            {
                _retirementErrorReported = true;
                application.FailInput(failed.Exception!.InnerException ?? failed.Exception);
            }
        }

        await application.Driver.DrainVisualsAsync();
        if (Page is not null)
        {
            await Page.DrainAsync();
        }
    }

    private async Task<UiVisualSnapshot?> UpdateLayerAsync(UiVisualSnapshot image, DrawList2D drawing, IReadOnlyList<UiVisualSnapshot> borrowed) =>
        await application.Driver.UpdateVisualAsync(image, drawing, borrowed) ? image : null;

    private async Task CloseAsync()
    {
        Page?.Stop();
        try
        {
            if (!_stopping && _published && application.State == UiApplicationState.Running && CaptureWindow() is { IsVisible: true, Presentation: UiWindowPresentation.Hosted })
            {
                surface.FreezeValueAnimations();
                surface.BeginVisualExit();
                long started = application.TimeProvider.GetTimestamp();
                while (application.State == UiApplicationState.Running && application.Driver.CanAnimate && CaptureWindow() is { IsVisible: true }
                    && surface.VisualEntries.Any(entry => entry.IsActive || entry.AwaitingPresentation)
                    && application.TimeProvider.GetElapsedTime(started) < Motion.MaximumVisualLifetime)
                {
                    surface.AdvanceVisualClock(application.AnimationClock.Now);
                    surface.UpdateLayout();
                    Advance();
                    await application.NextTurnAsync();
                }

                if (application.State == UiApplicationState.Running)
                {
                    surface.DeliverVisualNotifications();
                }
            }
        }
        finally
        {
            await DrainAsync();
        }
    }

    private void Track(UiVisualSnapshot image) => (_capturing ? _captureImages : _recorded).Add(image);

    private void Retire(UiPresenceEntry entry, Element element)
    {
        _elements.Remove(entry);
        element.Active.Dispose();
        element.Layer.Dispose();
        _retiring.Add(element.Layer.DrainAsync().AsTask());
    }

    private sealed class Element(IDisposable active, long created, UiVisualLayer layer, UiWindowSnapshot window, ulong epoch)
    {
        internal IDisposable Active { get; } = active;

        internal long Created { get; } = created;

        internal UiVisualLayer Layer { get; } = layer;

        internal Vector2 Scale { get; } = window.Scale;

        internal ulong Epoch { get; } = epoch;
    }
}
