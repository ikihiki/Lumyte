using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>Connects a element tree with caller-managed synchronization.</summary>
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
        Visit(_rootElement, null, Vector2.Zero, UiInputBounds.From(Vector2.Zero, _viewport), true, true, true);
        return new(_automationSurfaceId, checked(++_automationSequence), nodes.ToArray());

        void Visit(UiElement element, UiAutomationId? parent, Vector2 parentOrigin, UiInputBounds ancestorClip, bool visible, bool enabled, bool hitTestVisible)
        {
            Vector2 origin = UiInputGeometry.Origin(element, parentOrigin);
            var bounds = UiInputBounds.From(origin, element.Size);
            UiInputBounds clipped = bounds.Intersect(ancestorClip);
            visible &= element.LayoutVisible;
            enabled &= element.Widget.IsEnabled;
            hitTestVisible &= element.Widget.HitTestVisible;
            bool focusable = ReferenceEquals(element.InputVersion, _inputVersion) && element.InputFocusable;
            bool redacted = element.EffectiveAutomationExposure == UiAutomationExposure.RedactedSubtree;
            nodes.Add(new(element.AutomationId, parent, redacted ? null : element.Widget.TestId, redacted ? null : element.Widget.AutomationName, element.Widget.Role, Rectangle(bounds), Rectangle(clipped), visible, enabled, hitTestVisible, focusable, IsFocused(element), element.AutomationPressed, element.AutomationActivePress, element.SupportedActions, element.IsTransitioning));
            if (redacted)
            {
                return;
            }

            var distinct = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
            IEnumerable<UiElement> children = element.AutomationChildren ?? throw new InvalidOperationException("Semantic children cannot be null.");
            foreach (UiElement child in children)
            {
                if (child is null || !ReferenceEquals(child.Parent, element) || !ReferenceEquals(child.Surface, this) || !distinct.Add(child))
                {
                    throw new InvalidOperationException("Semantic children must be distinct connected direct children.");
                }

                Visit(child, element.AutomationId, origin, element.Widget.ClipToBounds ? clipped : ancestorClip, visible, enabled, hitTestVisible);
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
        UiElement? hit = UiInputGeometry.Contains(Vector2.Zero, _viewport, logicalPosition) ? _rootElement.FindInputHit(logicalPosition, _inputVersion) : null;
        for (UiElement? node = hit; node is not null; node = node.Parent)
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

    private static UiPointerProbe RedactProbe(UiElement? hit, UiAutomationId? target)
    {
        UiElement? boundary = null;
        for (UiElement? node = hit; node is not null; node = node.Parent)
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
