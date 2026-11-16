using System.Numerics;
using Lumyte.Core.Time;
using Lumyte.Graphics.Drawing2D;
using WebViewWidget = Lumyte.UI.WebView.Compose.Definitions.WebView;

namespace Lumyte.UI.WebView;

/// <summary>Records the latest borrowed browser texture without performing engine or GPU work.</summary>
public sealed class WebViewElement : UiElement<WebViewWidget>
{
    private bool _frameVisible = true;
    private Vector2 _viewportSize;

    internal WebViewElement(WebViewWidget widget)
        : base(widget)
    {
    }

    /// <inheritdoc/>
    protected override bool CacheRetainedDrawing => true;

    /// <inheritdoc/>
    protected override bool HasFrameAnimation => true;

    /// <inheritdoc/>
    protected override bool SupportsTransitionCapture => false;

    /// <inheritdoc/>
    protected override bool PreventsAncestorTransitionCapture => true;

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (Widget.Session!.Host is not { } host)
        {
            return UiInputResult.Unhandled;
        }

        if (context.Input is UiFocusChangedInput focus)
        {
            host.Focus(focus.IsFocused);
            return UiInputResult.Handled;
        }

        if (context.Input is UiPointerInput { Phase: UiPointerPhase.Down })
        {
            context.RequestFocus();
            context.CapturePointer();
        }

        host.Input(context.Input, context.PointerPosition);
        return UiInputResult.Handled;
    }

    /// <inheritdoc/>
    protected override UiInvalidation CompareConfiguration(WebViewWidget previous, WebViewWidget next) => ReferenceEquals(previous.Session, next.Session) ? UiInvalidation.None : UiInvalidation.All;

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(WebViewWidget next, UiElementUpdateContext context)
    {
        next.Session!.ValidateAttachment(this);
        next.Session.Host?.ValidateSurface(context.Surface);
        if (!ReferenceEquals(Widget.Session, next.Session))
        {
            context.CancelInput();
        }

        return null;
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = constraints.Constrain(new(800, 600));
        if (Surface is { } surface)
        {
            Widget.Session!.Host?.ValidateSurface(surface);
        }

        _viewportSize = size;
        Widget.Session!.Host?.Viewport(size, _frameVisible);
        return size;
    }

    /// <inheritdoc/>
    protected override void OnAnimationFrame(TimePoint? now)
    {
        _frameVisible = now is not null;
        Widget.Session!.Host?.Viewport(_viewportSize, _frameVisible);
    }

    /// <inheritdoc/>
    protected override void OnMounted()
    {
        Widget.Session!.Attach(this);
        Widget.Session.PixelsChanged += InvalidateDrawing;
        Widget.Session.FocusReturn += ReturnFocus;
    }

    /// <inheritdoc/>
    protected override void OnUnmounted()
    {
        Widget.Session!.FocusReturn -= ReturnFocus;
        Widget.Session.PixelsChanged -= InvalidateDrawing;
        Widget.Session.Detach(this);
    }

    /// <inheritdoc/>
    protected override void OnWidgetUpdated(WebViewWidget previous)
    {
        if (!ReferenceEquals(previous.Session, Widget.Session))
        {
            // Acquire first: an unexpected ownership violation must not detach the old session.
            Widget.Session!.Attach(this);
            previous.Session!.FocusReturn -= ReturnFocus;
            previous.Session.PixelsChanged -= InvalidateDrawing;
            previous.Session.Detach(this);
            Widget.Session.PixelsChanged += InvalidateDrawing;
            Widget.Session.FocusReturn += ReturnFocus;
        }
    }

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        if (Widget.Session!.Image is { } image && ContentSize.X > 0 && ContentSize.Y > 0)
        {
            canvas.DrawImage(image, new(0, 0, image.View.Texture.Width, image.View.Texture.Height), new(0, 0, ContentSize.X, ContentSize.Y));
            if (Widget.Session.PopupVisible && Widget.Session.PopupImage is { } popup)
            {
                canvas.DrawImage(popup, new(0, 0, popup.View.Texture.Width, popup.View.Texture.Height), Widget.Session.PopupBounds);
            }
        }
    }

    private void ReturnFocus(bool forward)
    {
        if (Surface is { } surface && ReferenceEquals(surface.FocusedElement, this))
        {
            surface.MoveFocus(forward ? UiFocusNavigation.Next : UiFocusNavigation.Previous);
        }
    }
}
