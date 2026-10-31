using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly Guid _automationSurfaceId = Guid.NewGuid();
    private ulong _automationIdentity;
    private ulong _automationSequence;

    /// <summary>Completes pending layout and copies the semantic tree; call sequentially with normal UI work.</summary>
    /// <returns>A detached immutable snapshot, without widgets or callbacks.</returns>
    public UiAutomationSnapshot CaptureAutomationSnapshot()
    {
        ThrowIfDisposed();
        UpdateLayout();
        var nodes = new List<UiAutomationNode>();
        Visit(_root, null, Vector2.Zero, UiInputBounds.From(Vector2.Zero, _viewport), true, true, true);
        return new(_automationSurfaceId, checked(++_automationSequence), nodes.ToArray());

        void Visit(Widget widget, UiAutomationId? parent, Vector2 parentOrigin, UiInputBounds ancestorClip, bool visible, bool enabled, bool hitTestVisible)
        {
            Vector2 origin = UiInputGeometry.Origin(widget, parentOrigin);
            var bounds = UiInputBounds.From(origin, widget.Size);
            UiInputBounds clipped = bounds.Intersect(ancestorClip);
            visible &= widget.LayoutVisible;
            enabled &= widget.IsEnabled;
            hitTestVisible &= widget.HitTestVisible;
            bool focusable = ReferenceEquals(widget.InputVersion, _inputVersion) && widget.InputFocusable;
            bool redacted = widget.EffectiveAutomationExposure == UiAutomationExposure.RedactedSubtree;
            nodes.Add(new(widget.AutomationId, parent, redacted ? null : widget.TestId, redacted ? null : widget.AutomationName, widget.Role, Rectangle(bounds), Rectangle(clipped), visible, enabled, hitTestVisible, focusable, IsFocused(widget), widget.AutomationPressed, widget.AutomationActivePress, widget.SupportedActions, widget.IsTransitioning));
            if (redacted)
            {
                return;
            }

            var distinct = new HashSet<Widget>(ReferenceEqualityComparer.Instance);
            IEnumerable<Widget> children = widget.AutomationChildren ?? throw new InvalidOperationException("Semantic children cannot be null.");
            foreach (Widget child in children)
            {
                if (child is null || !ReferenceEquals(child.Owner, widget) || !ReferenceEquals(child.Surface, this) || !distinct.Add(child))
                {
                    throw new InvalidOperationException("Semantic children must be distinct connected direct children.");
                }

                Visit(child, widget.AutomationId, origin, widget.ClipToBounds ? clipped : ancestorClip, visible, enabled, hitTestVisible);
            }
        }
    }

    /// <summary>Probes prepared pointer geometry and component routing without delivering input.</summary>
    /// <param name="logicalPosition">A finite surface-local position.</param>
    /// <returns>The hit element and accepting Click component, if present.</returns>
    public UiPointerProbe ProbePointer(Vector2 logicalPosition)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(logicalPosition.X) || !float.IsFinite(logicalPosition.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(logicalPosition));
        }

        UpdateLayout();
        Widget? hit = UiInputGeometry.Contains(Vector2.Zero, _viewport, logicalPosition) ? _root.FindInputHit(logicalPosition, _inputVersion) : null;
        for (Widget? node = hit; node is not null; node = node.Owner)
        {
            UiInputResult result = node.InvokePointerProbe(logicalPosition - node.InputOrigin);
            if (result != UiInputResult.Unhandled)
            {
                UiAutomationId? target = result == UiInputResult.Handled && (node.SupportedActions & UiAutomationActions.Click) != 0 ? node.AutomationId : null;
                return RedactProbe(hit, target);
            }
        }

        return RedactProbe(hit, null);
    }

    internal UiAutomationId AllocateAutomationId() => new(_automationSurfaceId, checked(++_automationIdentity));

    private static UiPointerProbe RedactProbe(Widget? hit, UiAutomationId? target)
    {
        Widget? boundary = null;
        for (Widget? node = hit; node is not null; node = node.Owner)
        {
            if (node.EffectiveAutomationExposure == UiAutomationExposure.RedactedSubtree)
            {
                boundary = node;
            }
        }

        return boundary is null ? new(hit?.AutomationId, target) : new(boundary.AutomationId, null);
    }

    private static Rect2D Rectangle(UiInputBounds bounds)
    {
        _ = UiLayoutMath.FiniteFloat(bounds.Right);
        _ = UiLayoutMath.FiniteFloat(bounds.Bottom);
        return new(
            UiLayoutMath.FiniteFloat(bounds.Left),
            UiLayoutMath.FiniteFloat(bounds.Top),
            UiLayoutMath.FiniteFloat(Math.Max(0, bounds.Right - bounds.Left)),
            UiLayoutMath.FiniteFloat(Math.Max(0, bounds.Bottom - bounds.Top)));
    }
}
