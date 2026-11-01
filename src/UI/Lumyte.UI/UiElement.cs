using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>A persistent UI instance with independent configuration, topology and lifetime.</summary>
public partial class UiElement
{
    private Widget _widget;
    private UiElementChildren[] _slots = [];
    private IReadOnlyList<UiElement> _children = Array.Empty<UiElement>();

    /// <summary>Initializes a new instance of the <see cref="UiElement"/> class.</summary>
    /// <param name="widget">The initial passive configuration.</param>
    protected internal UiElement(Widget widget)
    {
        ArgumentNullException.ThrowIfNull(widget);
        _widget = widget;
    }

    /// <summary>Gets the current adopted configuration.</summary>
    public Widget Widget => _widget;

    /// <summary>Gets the mounted owning surface, or null outside the mounted lifetime.</summary>
    public UiSurface? Surface { get; private set; }

    /// <summary>Gets the mounted parent, independent of declaration replacement.</summary>
    public UiElement? Parent { get; private set; }

    /// <summary>Gets the mounted children in component slot order.</summary>
    public IReadOnlyList<UiElement> Children => _children;

    /// <summary>Gets a value indicating whether this logical instance is mounted.</summary>
    public bool IsMounted => Surface is not null;

    internal IReadOnlyList<UiElementChildren> Slots => _slots;

    internal bool UsesTreeValidation { get; set; }

    internal Action? Prepare(Widget next, UiElementUpdateContext context) => PrepareUpdate(next, context);

    internal void Publish(UiSurface surface, Widget widget, IReadOnlyList<UiElement>[] slots)
    {
        _widget = widget;
        Surface = surface;
        bool changed = _slots.Length != slots.Length;
        if (changed)
        {
            var storage = new UiElementChildren[slots.Length];
            for (int index = 0; index < storage.Length; index++)
            {
                storage[index] = index < _slots.Length ? _slots[index] : new(this, index);
            }

            for (int index = slots.Length; index < _slots.Length; index++)
            {
                _slots[index].Publish(Array.Empty<UiElement>());
            }

            _slots = storage;
        }

        for (int index = 0; index < slots.Length; index++)
        {
            changed |= _slots[index].Publish(slots[index]);
        }

        if (changed)
        {
            _children = _slots.Length == 1 ? _slots[0] : Array.AsReadOnly(_slots.SelectMany(slot => slot).ToArray());
        }
    }

    internal void AssignParent(UiElement? parent) => Parent = parent;

    internal void NotifyPublished(Widget? previous, bool childrenChanged)
    {
        if (!IsMounted)
        {
            return;
        }

        if (previous is null)
        {
            OnMounted();
        }
        else if (!ReferenceEquals(previous, Widget))
        {
            OnWidgetUpdated(previous);
        }

        if (IsMounted && childrenChanged)
        {
            OnChildrenChanged();
        }
    }

    internal void Unmount()
    {
        if (!IsMounted)
        {
            return;
        }

        Surface?.ForgetHostValidation(this);
        Surface = null;
        Parent = null;
        foreach (UiElement child in _children)
        {
            if (ReferenceEquals(child.Parent, this))
            {
                child.Parent = null;
            }
        }

        _children = Array.Empty<UiElement>();
        _slots = [];
        _inputChildren = Array.Empty<UiElement>();
        _inputSubtreeBounds = default;
        InputVersion = null;
        InputBounds = default;
        InputOrigin = default;
        OnUnmounted();
    }

    /// <summary>Gets this instance's stable runtime view for a slot of its current configuration.</summary>
    /// <param name="declarationSlot">A slot belonging to the current configuration.</param>
    /// <returns>The current mounted child instances.</returns>
    protected UiElementChildren GetChildren(WidgetChildren declarationSlot)
    {
        ArgumentNullException.ThrowIfNull(declarationSlot);
        if (!ReferenceEquals(declarationSlot.Owner, Widget))
        {
            throw new ArgumentException("The slot must belong to the current configuration.", nameof(declarationSlot));
        }

        return _slots[declarationSlot.SlotIndex];
    }

    /// <summary>Prepares custom state changes without modifying the live instance.</summary>
    /// <param name="next">The configuration proposed for publication.</param>
    /// <param name="context">The proposed direct child instances.</param>
    /// <returns>An optional synchronous publication action.</returns>
    protected virtual Action? PrepareUpdate(Widget next, UiElementUpdateContext context) => null;

    /// <summary>Starts external resources after the complete topology is published.</summary>
    protected virtual void OnMounted()
    {
    }

    /// <summary>Observes an adopted configuration; ordinary instance fields are already preserved.</summary>
    /// <param name="previous">The previous configuration.</param>
    protected virtual void OnWidgetUpdated(Widget previous)
    {
    }

    /// <summary>Observes a change in direct child identities, order or slots.</summary>
    protected virtual void OnChildrenChanged()
    {
    }

    /// <summary>Ends external resources after removal or surface disposal.</summary>
    protected virtual void OnUnmounted()
    {
    }
}
