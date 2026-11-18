using System.Numerics;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Owns one realized content tree per mounted repeater.</summary>
/// <typeparam name="T">The item type.</typeparam>
public class ItemsRepeaterElement<T> : UiElement<Compose.Definitions.ItemsRepeater<T>>
    where T : notnull
{
    private T[] _itemSnapshot = [];
    private string[] _keySnapshot = [];

    /// <summary>Initializes a new instance of the <see cref="ItemsRepeaterElement{T}"/> class.</summary>
    /// <param name="widget">The declaration.</param>
    protected internal ItemsRepeaterElement(Compose.Definitions.ItemsRepeater<T> widget)
        : base(widget)
    {
    }

    /// <summary>Gets the adopted item snapshot.</summary>
    public IReadOnlyList<T> Items { get; private set; } = Array.Empty<T>();

    /// <summary>Gets the adopted ordinal keys.</summary>
    public IReadOnlyList<string> Keys { get; private set; } = Array.Empty<string>();

    /// <summary>Gets a value indicating whether an internal range update uses the adopted model.</summary>
    protected virtual bool UseAdoptedItems => false;

    /// <inheritdoc/>
    protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context)
    {
        var declaration = (Compose.Definitions.ItemsRepeater<T>)next;
        if (UseAdoptedItems)
        {
            return PrepareRealization(declaration, _itemSnapshot, _keySnapshot);
        }

        ArgumentNullException.ThrowIfNull(declaration.Items);
        ArgumentNullException.ThrowIfNull(declaration.KeySelector);
        ArgumentNullException.ThrowIfNull(declaration.ItemTemplate);
        if (!Enum.IsDefined(declaration.Orientation))
        {
            throw new ArgumentOutOfRangeException(nameof(next));
        }

        T[] items = declaration.Items.ToArray();
        string[] keys = new string[items.Length];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < items.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(items[index]);
            string key = declaration.KeySelector(items[index]);
            if (string.IsNullOrEmpty(key) || !unique.Add(key))
            {
                throw new ArgumentException("Item keys must be nonempty and ordinal unique.", nameof(next));
            }

            keys[index] = key;
        }

        return PrepareRealization(declaration, items, keys);
    }

    /// <summary>Prepares content from a validated copied item snapshot.</summary>
    /// <param name="declaration">The proposed declaration.</param>
    /// <param name="items">The copied items.</param>
    /// <param name="keys">The validated keys.</param>
    /// <returns>Content and publication state.</returns>
    protected virtual UiElementContent PrepareRealization(Compose.Definitions.ItemsRepeater<T> declaration, T[] items, string[] keys)
    {
        var roots = new Widget[items.Length];
        var seen = new HashSet<Widget>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < items.Length; index++)
        {
            Widget root = declaration.ItemTemplate!(items[index]) ?? throw new InvalidOperationException("An item template returned null.");
            ValidateTemplate(root, seen);
            roots[index] = WrapItem(declaration, items[index], keys[index], root);
        }

        string generation = Guid.NewGuid().ToString("N");
        var panel = new Lumyte.UI.Compose.Definitions.Stack { Key = generation, Orientation = declaration.Orientation };
        panel.Children.Initialize(roots);
        Widget content = WrapContent(declaration, panel);
        return AdoptContent(declaration, items, keys, content).WithItemOwnership(roots);
    }

    /// <summary>Creates an atomic adoption action for prepared content.</summary>
    /// <param name="declaration">The next declaration.</param>
    /// <param name="items">The copied items.</param>
    /// <param name="keys">The validated keys.</param>
    /// <param name="content">The new content.</param>
    /// <returns>The preparation result.</returns>
    protected UiElementContent AdoptContent(Compose.Definitions.ItemsRepeater<T> declaration, T[] items, string[] keys, Widget content)
    {
        Action? commit = PrepareItems(declaration, items, keys);
        return new(content, () =>
        {
            _itemSnapshot = items;
            _keySnapshot = keys;
            Items = Array.AsReadOnly(items);
            Keys = Array.AsReadOnly(keys);
            commit?.Invoke();
        });
    }

    /// <summary>Wraps content without publishing state.</summary>
    /// <param name="declaration">The next declaration.</param>
    /// <param name="content">The realized panel.</param>
    /// <returns>The content root.</returns>
    protected virtual Widget WrapContent(Compose.Definitions.ItemsRepeater<T> declaration, Widget content) => content;

    /// <summary>Wraps an item's content in an actual semantic owner when needed.</summary>
    /// <param name="declaration">The next declaration.</param>
    /// <param name="item">The item.</param>
    /// <param name="key">The ordinal key.</param>
    /// <param name="content">The fresh content root.</param>
    /// <returns>The item root.</returns>
    protected virtual Widget WrapItem(Compose.Definitions.ItemsRepeater<T> declaration, T item, string key, Widget content) => content;

    /// <summary>Prepares state adoption without mutating the current tree.</summary>
    /// <param name="declaration">The next declaration.</param>
    /// <param name="items">The copied items.</param>
    /// <param name="keys">Validated keys.</param>
    /// <returns>The publication action.</returns>
    protected virtual Action? PrepareItems(Compose.Definitions.ItemsRepeater<T> declaration, T[] items, string[] keys) => null;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        UiElement content = Children[0];
        Vector2 size = LayoutChild(content, constraints);
        SetChildOffset(content, Vector2.Zero);
        return constraints.Constrain(size);
    }

    private static void ValidateTemplate(Widget root, HashSet<Widget> seen)
    {
        root.ValidateFreshItemRoot();
        var pending = new System.Collections.Generic.Stack<Widget>();
        pending.Push(root);
        while (pending.TryPop(out Widget? node))
        {
            if (node.MountedInstanceCount != 0 || !seen.Add(node))
            {
                throw new InvalidOperationException("Item trees must be disjoint and acyclic.");
            }

            foreach (Widget child in node.GetDeclaredItemChildren())
            {
                pending.Push(child);
            }
        }
    }
}
