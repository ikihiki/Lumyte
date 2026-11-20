using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>A lazy projection of planned changes over unchanged persistent branches.</summary>
internal sealed class UiProposedTree
{
    private readonly UiElement _root;
    private readonly Dictionary<UiElement, (Widget Next, IReadOnlyList<UiElement>[] Slots)> _updates = new(ReferenceEqualityComparer.Instance);
    private Dictionary<Widget, UiElement>? _elements;
    private Dictionary<UiElement, UiElement>? _parents;
    private HashSet<Widget>? _shared;
    private IReadOnlyList<Widget>? _nodes;

    internal UiProposedTree(UiElement root, IEnumerable<(UiElement Element, Widget Next, IReadOnlyList<UiElement>[] Slots)> updates)
    {
        _root = root;
        foreach ((UiElement element, Widget next, IReadOnlyList<UiElement>[] slots) in updates)
        {
            _updates.Add(element, (next, slots));
        }
    }

    internal IReadOnlyList<Widget> Nodes
    {
        get
        {
            EnsureIndex();
            return _nodes!;
        }
    }

    internal IReadOnlyList<Widget> GetChildren(Widget widget, UiElement? receiver)
    {
        EnsureIndex();
        return Array.AsReadOnly(Children(Resolve(widget, receiver)).Select(Configuration).ToArray());
    }

    internal Widget? GetParent(Widget widget, UiElement? receiver)
    {
        EnsureIndex();
        return _parents!.TryGetValue(Resolve(widget, receiver), out UiElement? parent) ? Configuration(parent) : null;
    }

    internal IReadOnlyList<Widget> GetSlotChildren(WidgetChildren slot, UiElement? receiver)
    {
        EnsureIndex();
        if (!_elements!.ContainsKey(slot.Owner))
        {
            throw new ArgumentException("The slot owner is outside the proposed tree.", nameof(slot));
        }

        UiElement owner = Resolve(slot.Owner, receiver);
        if (slot.IsPortal || slot.SlotIndex >= Configuration(owner).ChildCollections.Count
            || !ReferenceEquals(Configuration(owner).ChildCollections[slot.SlotIndex], slot))
        {
            throw new ArgumentException("The slot must be non-portal content of the proposed owner.", nameof(slot));
        }

        IReadOnlyList<UiElement> children = _updates.TryGetValue(owner, out (Widget Next, IReadOnlyList<UiElement>[] Slots) update)
            ? update.Slots[slot.SlotIndex] : owner.Slots[slot.SlotIndex];
        return Array.AsReadOnly(children.Select(Configuration).ToArray());
    }

    internal bool IsOrdinaryChild(Widget widget, UiElement? receiver, UiParentDataKind kind)
    {
        EnsureIndex();
        UiElement child = Resolve(widget, receiver);
        if (!_parents!.TryGetValue(child, out UiElement? parent))
        {
            return false;
        }

        Widget configuration = Configuration(parent);
        WidgetChildren? ordinary = kind switch
        {
            UiParentDataKind.Grid => (configuration as Compose.Definitions.Grid)?.Children,
            UiParentDataKind.Flex => (configuration as Compose.Definitions.Flex)?.Children,
            UiParentDataKind.SplitView => (configuration as Compose.Definitions.SplitView)?.Children,
            _ => null,
        };
        if (ordinary is null)
        {
            return false;
        }

        IReadOnlyList<UiElement> children = _updates.TryGetValue(parent, out (Widget Next, IReadOnlyList<UiElement>[] Slots) update) ? update.Slots[ordinary.SlotIndex] : parent.Slots[ordinary.SlotIndex];
        return children.Contains(child);
    }

    internal UiUseEnvironment? Environment(UiElement element)
    {
        var ancestors = new System.Collections.Generic.Stack<UiElement>();
        for (UiElement? current = element; current is not null; current = Parent(current))
        {
            ancestors.Push(current);
        }

        UiUseEnvironment? environment = null;
        while (ancestors.TryPop(out UiElement? current))
        {
            environment = UiUseEnvironment.Overlay(environment, Configuration(current).ProvidedContexts);
        }

        return environment;
    }

    internal bool Contains(UiElement element)
    {
        EnsureIndex();
        return ReferenceEquals(element, _root) || _parents!.ContainsKey(element);
    }

    internal UiElement? Parent(UiElement element)
    {
        EnsureIndex();
        return _parents!.GetValueOrDefault(element);
    }

    internal IReadOnlyList<UiElement>[] Slots(UiElement element) => _updates.TryGetValue(element, out (Widget Next, IReadOnlyList<UiElement>[] Slots) update)
        ? update.Slots : element.Slots.Select(slot => (IReadOnlyList<UiElement>)slot).ToArray();

    internal Widget Configuration(UiElement element) => _updates.TryGetValue(element, out (Widget Next, IReadOnlyList<UiElement>[] Slots) update) ? update.Next : element.Widget;

    internal IEnumerable<UiElement> Children(UiElement element) => _updates.TryGetValue(element, out (Widget Next, IReadOnlyList<UiElement>[] Slots) update) ? update.Slots.SelectMany(slot => slot) : element.Children;

    private UiElement Resolve(Widget widget, UiElement? receiver)
    {
        for (UiElement? current = receiver; current is not null; current = _parents!.GetValueOrDefault(current))
        {
            if (ReferenceEquals(Configuration(current), widget))
            {
                return current;
            }
        }

        if (_shared!.Contains(widget))
        {
            throw new ArgumentException("A shared declaration must be inspected from its own validation context.", nameof(widget));
        }

        return _elements![widget];
    }

    private void EnsureIndex()
    {
        if (_elements is not null)
        {
            return;
        }

        _elements = new(ReferenceEqualityComparer.Instance);
        _parents = new(ReferenceEqualityComparer.Instance);
        _shared = new(ReferenceEqualityComparer.Instance);
        var nodes = new List<Widget>();
        var pending = new System.Collections.Generic.Stack<(UiElement Element, UiElement? Parent)>();
        pending.Push((_root, null));
        while (pending.TryPop(out (UiElement Element, UiElement? Parent) item))
        {
            Widget widget = Configuration(item.Element);
            if (!_elements.TryAdd(widget, item.Element))
            {
                _shared.Add(widget);
            }

            nodes.Add(widget);
            if (item.Parent is not null)
            {
                _parents.Add(item.Element, item.Parent);
            }

            foreach (UiElement child in Children(item.Element).Reverse())
            {
                pending.Push((child, item.Element));
            }
        }

        _nodes = nodes.AsReadOnly();
    }
}
