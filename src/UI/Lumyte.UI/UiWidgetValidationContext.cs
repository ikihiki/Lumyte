using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>A read-only projection of the proposed surface topology before any live ownership changes.</summary>
public sealed class UiWidgetValidationContext
{
    private readonly UiProposedTree _tree;
    private readonly UiElement? _receiver;

    internal UiWidgetValidationContext(UiProposedTree tree, UiElement? receiver = null)
    {
        _tree = tree;
        _receiver = receiver;
    }

    /// <summary>Gets all non-portal nodes in the proposed surface.</summary>
    public IReadOnlyList<Widget> Nodes
    {
        get
        {
            UsesTree = true;
            return _tree.Nodes;
        }
    }

    internal bool UsesTree { get; private set; }

    /// <summary>Gets the proposed direct children, excluding portal declarations' content.</summary>
    /// <param name="widget">The parent to inspect.</param>
    /// <returns>The projected children.</returns>
    public IReadOnlyList<Widget> GetChildren(Widget widget)
    {
        UsesTree = true;
        return _tree.GetChildren(widget, _receiver);
    }

    /// <summary>Gets proposed children in one declaration slot, including runtime replacements.</summary>
    /// <param name="slot">The proposed owner's non-portal slot; stale or unrelated slots are rejected.</param>
    /// <returns>The projected slot content.</returns>
    /// <exception cref="ArgumentNullException">The slot is null.</exception>
    /// <exception cref="ArgumentException">The slot is stale, portal content, outside the tree, or ambiguous.</exception>
    public IReadOnlyList<Widget> GetSlotChildren(WidgetChildren slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        UsesTree = true;
        return _tree.GetSlotChildren(slot, _receiver);
    }

    /// <summary>Gets the proposed parent, including new owners of retained nodes.</summary>
    /// <param name="widget">The node to inspect.</param>
    /// <returns>The proposed parent, or null for the root.</returns>
    public Widget? GetParent(Widget widget)
    {
        UsesTree = true;
        return _tree.GetParent(widget, _receiver);
    }
}
