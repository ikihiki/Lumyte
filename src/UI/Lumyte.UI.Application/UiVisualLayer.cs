using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Application;

/// <summary>Retains drawing commands and refreshes an optional group-opacity raster cache.</summary>
internal sealed class UiVisualLayer(UiApplication application, UiVisualCoordinator coordinator, Func<UiElement?> resolve, bool needsBuffer, Func<IReadOnlySet<UiElement>?>? exclusions = null, bool includeAncestors = false) : IDisposable
{
    private readonly List<Task> _retiring = [];
    private readonly List<IDisposable> _borrowedLeases = [];
    private DrawList2D? _drawing;
    private UiVisualSnapshot[] _borrowed = [];
    private Task<UiVisualSnapshot?>? _pending;
    private Vector2 _size;
    private ulong _revision;
    private UiAutomationId _identity;
    private ulong _version;
    private ulong _uploadedVersion;
    private ulong _pendingVersion;
    private bool _needsBuffer = needsBuffer;
    private bool _dynamic;
    private bool _disposed;

    internal UiVisualSnapshot? Image { get; private set; }

    internal long PreparationStarted { get; private set; } = application.TimeProvider.GetTimestamp();

    internal bool Ready => !_needsBuffer || Image is not null;

    internal bool Failed { get; private set; }

    internal string FailureCode { get; private set; } = "capture-unavailable";

    internal Rect2D? CurrentBounds => resolve() is { } widget && ReferenceEquals(widget.Surface, coordinator.Surface) ? UiVisualCoordinator.Bounds(widget) : null;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RetireImage();
        if (_pending is { } pending)
        {
            _retiring.Add(RetirePendingAsync(pending));
            _pending = null;
        }

        ReleaseDrawing();
    }

    internal void RequireOpacityBuffer(bool required)
    {
        if (required && !_needsBuffer)
        {
            PreparationStarted = application.TimeProvider.GetTimestamp();
            _needsBuffer = true;
        }
    }

    internal void Advance()
    {
        if (_disposed || Failed)
        {
            return;
        }

        if (_pending is { IsCompleted: true } pending)
        {
            _pending = null;
            UiVisualSnapshot? image = pending.GetAwaiter().GetResult();
            if (image is null)
            {
                Failed = true;
                return;
            }

            if (!ReferenceEquals(Image, image))
            {
                RetireImage();
                Image = image;
            }

            _uploadedVersion = _pendingVersion;
        }

        if (!_needsBuffer || _pending is not null || !Refresh())
        {
            return;
        }

        if (_uploadedVersion != _version)
        {
            _pendingVersion = _version;
            _pending = coordinator.RasterizeLayerAsync(_drawing!, _size, _borrowed, Image);
            FailureCode = coordinator.CaptureFailureCode;
        }
    }

    internal async ValueTask PrepareAsync(CancellationToken token)
    {
        long started = application.TimeProvider.GetTimestamp();
        Advance();
        while (!_disposed && !Ready && !Failed)
        {
            TimeSpan remaining = application.Motion.PreparationTimeout - application.TimeProvider.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException("Layer preparation exceeded its deadline.");
            }

            await application.NextTurnAsync().AsTask().WaitAsync(remaining, application.TimeProvider, token);
            Advance();
        }
    }

    internal void Draw(DrawingCanvas2D canvas, Rect2D bounds, UiVisualPose pose)
    {
        if (_disposed || pose.Opacity == 0)
        {
            return;
        }

        if (pose.Opacity != 1)
        {
            RequireOpacityBuffer(true);
            Advance();
            if (Image is { } image)
            {
                coordinator.Draw(canvas, image, bounds, pose);
                return;
            }

            // Preserve content while preparing the new capability, or fall back if capture is unavailable.
            Draw(canvas, bounds, pose with { Opacity = 1 });

            return;
        }

        if (!Refresh())
        {
            return;
        }

        foreach (UiVisualSnapshot borrowed in _borrowed)
        {
            coordinator.TrackImage(borrowed);
        }

        canvas.Save();
        try
        {
            Vector2 center = new(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
            canvas.Translate(center + pose.Offset);
            canvas.Scale(pose.Scale);
            canvas.Translate(-center);
            canvas.Translate(new(bounds.X, bounds.Y));
            canvas.Scale(new(bounds.Width / _size.X, bounds.Height / _size.Y));
            canvas.Replay(_drawing!);
        }
        finally
        {
            canvas.Restore();
        }
    }

    internal async ValueTask DrainAsync()
    {
        Dispose();
        await Task.WhenAll(_retiring);
    }

    private static async Task RetirePendingAsync(Task<UiVisualSnapshot?> task)
    {
        UiVisualSnapshot? image = await task;
        image?.Dispose();
        if (image is not null)
        {
            await image.Released;
        }
    }

    private bool Refresh()
    {
        UiElement? widget = resolve();
        if (widget is null || !ReferenceEquals(widget.Surface, coordinator.Surface) || widget.Size.X <= 0 || widget.Size.Y <= 0)
        {
            return false;
        }

        if (_drawing is not null && !_dynamic && _revision == widget.VisualRevision && _identity == widget.AutomationId && _size == widget.Size)
        {
            return true;
        }

        (DrawList2D drawing, UiVisualSnapshot[] borrowed) = coordinator.RecordLayer(widget, exclusions?.Invoke(), includeAncestors);
        IDisposable[] leases = borrowed.Select(image => image.Retain()).ToArray();
        ReleaseDrawing();
        _borrowedLeases.AddRange(leases);
        _drawing = drawing;
        _borrowed = borrowed;
        _size = widget.Size;
        _revision = widget.VisualRevision;
        _identity = widget.AutomationId;
        _dynamic = widget.EnumerateSubtree().Any(node => !node.TransitionCaptureSupported || (!ReferenceEquals(node, widget) && node.Presence?.IsActive == true));
        _version++;
        return true;
    }

    private void RetireImage()
    {
        if (Image is { } image)
        {
            Image = null;
            image.Dispose();
            _retiring.Add(image.Released);
        }
    }

    private void ReleaseDrawing()
    {
        foreach (IDisposable lease in _borrowedLeases)
        {
            lease.Dispose();
        }

        _borrowedLeases.Clear();
        _borrowed = [];
        _drawing = null;
    }
}
