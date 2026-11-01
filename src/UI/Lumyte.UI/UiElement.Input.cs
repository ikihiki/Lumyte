using System.Numerics;

namespace Lumyte.UI;

/// <summary>A persistent UI instance with independent configuration, topology and lifetime.</summary>
public partial class UiElement
{
    private IReadOnlyList<UiElement> _inputChildren = Array.Empty<UiElement>();
    private UiInputBounds _inputSubtreeBounds;

    internal object? InputVersion { get; private set; }

    internal UiInputBounds InputBounds { get; private set; }

    internal Vector2 InputOrigin { get; private set; }

    internal UiInputResult InvokeInput(in UiInputContext context) => OnInput(in context);

    internal void InvokePointerCaptureChanged(UiPointerCaptureState state) => OnPointerCaptureChanged(state);

    internal void UpdateInput(Vector2 parentOrigin, UiInputBounds ancestorClip, object version)
    {
        if (!UiInputGeometry.AcceptsInput(this))
        {
            return;
        }

        Vector2 origin = UiInputGeometry.Origin(this, parentOrigin);
        var bounds = UiInputBounds.From(origin, Size);
        UiInputBounds clip = Widget.ClipToBounds ? ancestorClip.Intersect(bounds) : ancestorClip;
        InputVersion = version;
        InputFocusable = CanReceiveFocus;
        InputBounds = bounds.Intersect(clip);
        InputOrigin = origin;
        _inputSubtreeBounds = InputBounds;
        _inputChildren = ResolveInputChildren();
        for (int index = 0; index < _inputChildren.Count; index++)
        {
            UiElement child = _inputChildren[index];
            child.UpdateInput(origin, clip, version);
            if (ReferenceEquals(child.InputVersion, version))
            {
                _inputSubtreeBounds = _inputSubtreeBounds.Union(child._inputSubtreeBounds);
            }
        }
    }

    internal UiElement? FindInputHit(Vector2 position, object? version)
    {
        if (!ReferenceEquals(InputVersion, version) || !_inputSubtreeBounds.Contains(position))
        {
            return null;
        }

        for (int index = _inputChildren.Count - 1; index >= 0; index--)
        {
            UiElement? hit = _inputChildren[index].FindInputHit(position, version);
            if (hit is not null)
            {
                return hit;
            }
        }

        return InputBounds.Contains(position) ? this : null;
    }

    /// <summary>Handles one logical input; return Unhandled to offer it to the owning parent.</summary>
    /// <param name="context">The synchronous delivery context; do not retain it.</param>
    /// <returns>Consumption and propagation behavior.</returns>
    protected virtual UiInputResult OnInput(in UiInputContext context) => UiInputResult.Unhandled;

    /// <summary>Observes capture acquisition, refreshed input geometry or loss after the surface changes ownership.</summary>
    /// <param name="state">The current capture state; loss is also reported before Up and cancellation.</param>
    protected virtual void OnPointerCaptureChanged(UiPointerCaptureState state)
    {
    }

    /// <summary>Returns direct input children in back-to-front order; the default includes every registered group.</summary>
    /// <returns>The children selected by this component, without duplicates.</returns>
    protected virtual IEnumerable<UiElement> GetInputChildren() => Slots.Count switch
    {
        0 => Array.Empty<UiElement>(),
        1 => Slots[0],
        _ => EnumerateInputCollections(),
    };

    /// <summary>Invalidates component-owned runtime input state; declaration setters do not call this.</summary>
    protected void InvalidateInput() => Surface?.InvalidateInput();

    private IReadOnlyList<UiElement> ResolveInputChildren()
    {
        IEnumerable<UiElement> children = GetInputChildren()
            ?? throw new InvalidOperationException("Input children cannot be null.");
        if (children is UiElementChildren owned)
        {
            if (owned.Count != 0 && !ReferenceEquals(owned[0].Parent, this))
            {
                throw new InvalidOperationException("Input children must be direct children.");
            }

            return owned;
        }

        if (children is UiElement[] { Length: 0 })
        {
            return Array.Empty<UiElement>();
        }

        var snapshot = new List<UiElement>();
        var included = new HashSet<UiElement>(ReferenceEqualityComparer.Instance);
        foreach (UiElement child in children)
        {
            if (child is null || !ReferenceEquals(child.Parent, this) || !included.Add(child))
            {
                throw new InvalidOperationException("Input children must be distinct direct children.");
            }

            snapshot.Add(child);
        }

        return snapshot.ToArray();
    }

    private IEnumerable<UiElement> EnumerateInputCollections()
    {
        foreach (UiElementChildren collection in Slots)
        {
            foreach (UiElement child in collection)
            {
                yield return child;
            }
        }
    }
}
