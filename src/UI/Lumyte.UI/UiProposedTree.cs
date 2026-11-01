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

    private Widget Configuration(UiElement element) => _updates.TryGetValue(element, out (Widget Next, IReadOnlyList<UiElement>[] Slots) update) ? update.Next : element.Widget;

    private IEnumerable<UiElement> Children(UiElement element) => _updates.TryGetValue(element, out (Widget Next, IReadOnlyList<UiElement>[] Slots) update) ? update.Slots.SelectMany(slot => slot) : element.Children;

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
