using System.Numerics;
using Lumyte.Core.Time;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Owns admitted notification content in the normal tree without taking keyboard focus.</summary>
public class ToastHostElement : UiElement<Compose.Definitions.ToastHost>
{
    private ToastController? _controller;

    /// <summary>Initializes a new instance of the <see cref="ToastHostElement"/> class.</summary>
    /// <param name="widget">The passive host declaration.</param>
    protected internal ToastHostElement(Compose.Definitions.ToastHost widget)
        : base(widget)
    {
    }

    internal bool IsPresented => IsMounted && Surface!.AreFrameUpdatesEnabled && !Surface.PageTransitioning
        && Size.X > 0 && Size.Y > 0 && IsContentActive && AncestorsVisible();

    internal void Refresh()
    {
        Surface?.RebuildSubtree(this, Widget, refreshContent: true);
        RegisterFrames();
    }

    internal bool Borrows(ToastController controller) => IsMounted && ReferenceEquals(_controller, controller);

    internal void Stopped() => Surface?.RemoveFrameAnimation(this);

    /// <inheritdoc/>
    protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context)
    {
        var declaration = (Compose.Definitions.ToastHost)next;
        ArgumentNullException.ThrowIfNull(declaration.Controller);
        Widget content = declaration.Controller.Prepare(this, context, out Action commit);
        return new UiElementContent(content, () =>
        {
            if (_controller != declaration.Controller)
            {
                _controller?.Detach(this);
                _controller = declaration.Controller;
            }

            commit();
        }).WithFreshItemOwnership(content);
    }

    /// <inheritdoc/>
    protected override void OnMounted() => RegisterFrames();

    /// <inheritdoc/>
    protected override void OnWidgetUpdated(Compose.Definitions.ToastHost previous) => RegisterFrames();

    /// <inheritdoc/>
    protected override void OnUnmounted()
    {
        _controller?.Detach(this);
        _controller = null;
    }

    /// <inheritdoc/>
    protected override bool HitTestSelf(Vector2 localPosition) => false;

    /// <inheritdoc/>
    protected override void OnAnimationFrame(TimePoint? now)
    {
        if (now is not null && _controller?.HasExpiryWork == true)
        {
            _controller?.Advance();
        }
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        UiElement content = Children[0];
        Vector2 size = LayoutChild(content, constraints.Loosen());
        SetChildOffset(content, Vector2.Zero);
        return constraints.Constrain(size);
    }

    private bool AncestorsVisible()
    {
        for (UiElement? node = this; node is not null; node = node.Parent)
        {
            if (!node.Widget.IsVisible || node.PresenceState == UiPresenceState.Hidden)
            {
                return false;
            }
        }

        return true;
    }

    private void RegisterFrames()
    {
        if (_controller?.HasExpiryWork == true)
        {
            Surface?.AddFrameAnimation(this);
        }
        else
        {
            Surface?.RemoveFrameAnimation(this);
        }
    }
}
