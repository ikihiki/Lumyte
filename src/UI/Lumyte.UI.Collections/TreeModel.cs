namespace Lumyte.UI.Collections;

internal sealed class TreeModel<T>
    where T : notnull
{
    internal const int MaximumNodes = 1000000;
    internal const int MaximumDepth = 256;

    private readonly Dictionary<string, Node> _known;
    private readonly Dictionary<string, string[]> _children;
    private readonly string[] _roots;

    private TreeModel(Dictionary<string, Node> known, Dictionary<string, string[]> children, string[] roots)
    {
        _known = known;
        _children = children;
        _roots = roots;
    }

    internal IReadOnlyDictionary<string, Node> Known => _known;

    internal static TreeModel<T> Create(Compose.Definitions.TreeView<T> declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration.Roots);
        ArgumentNullException.ThrowIfNull(declaration.KeySelector);
        ArgumentNullException.ThrowIfNull(declaration.ChildrenSelector);
        ArgumentNullException.ThrowIfNull(declaration.HasChildren);
        if (!declaration.HasItemTemplate)
        {
            ArgumentNullException.ThrowIfNull(declaration.ItemTemplate);
        }

        if (!float.IsFinite(declaration.Indentation) || declaration.Indentation < 0 || ((double)declaration.Indentation * MaximumDepth) + 16 > float.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(declaration));
        }

        var model = new TreeModel<T>(new(StringComparer.Ordinal), new(StringComparer.Ordinal), []);
        string[] roots = model.Add(declaration, declaration.Roots, null, 0);
        return new(model._known, model._children, roots);
    }

    internal TreeModel<T> Copy() => new(new(_known, StringComparer.Ordinal), new(_children, StringComparer.Ordinal), _roots);

    internal Node[] Flatten(Compose.Definitions.TreeView<T> declaration, HashSet<string> expanded)
    {
        var rows = new List<Node>();
        foreach (string root in _roots)
        {
            Visit(root);
        }

        return rows.ToArray();

        void Visit(string key)
        {
            Node node = _known[key];
            if (!node.Visible)
            {
                return;
            }

            rows.Add(node);
            if (!node.CanExpand || !expanded.Contains(key))
            {
                return;
            }

            if (!_children.TryGetValue(key, out string[]? children))
            {
                IReadOnlyList<T> source = declaration.ChildrenSelector!(node.Item) ?? throw new InvalidOperationException("A child selector returned null.");
                children = Add(declaration, source, key, node.Depth + 1);
                _children.Add(key, children);
            }

            foreach (string child in children)
            {
                Visit(child);
            }
        }
    }

    internal void RevealPath(Compose.Definitions.TreeView<T> declaration, IReadOnlyList<string> path, HashSet<string> expanded)
    {
        for (int index = 0; index < path.Count; index++)
        {
            string key = path[index];
            if (!_known.TryGetValue(key, out Node? node) || node.Parent != (index == 0 ? null : path[index - 1]) || !node.Visible || !node.Enabled)
            {
                throw new ArgumentException("The explicit key path is unavailable or does not match its parents.", nameof(path));
            }

            if (index == path.Count - 1)
            {
                break;
            }

            if (!node.CanExpand)
            {
                throw new ArgumentException("A path ancestor cannot expand.", nameof(path));
            }

            if (!_children.ContainsKey(key))
            {
                IReadOnlyList<T> source = declaration.ChildrenSelector!(node.Item) ?? throw new InvalidOperationException("A child selector returned null.");
                _children.Add(key, Add(declaration, source, key, node.Depth + 1));
            }

            expanded.Add(key);
        }
    }

    private string[] Add(Compose.Definitions.TreeView<T> declaration, IReadOnlyList<T> source, string? parent, int depth)
    {
        int count = source.Count;
        if (count < 0 || count > MaximumNodes - _known.Count || (count != 0 && depth > MaximumDepth))
        {
            throw new ArgumentException("The discovered hierarchy exceeds its bounded node count or depth.", nameof(source));
        }

        var snapshot = new T[count];
        for (int index = 0; index < count; index++)
        {
            snapshot[index] = source[index];
        }

        string[] keys = new string[count];
        for (int index = 0; index < count; index++)
        {
            T item = snapshot[index];
            ArgumentNullException.ThrowIfNull(item);
            string key = declaration.KeySelector!(item);
            if (string.IsNullOrEmpty(key) || key.Length > 256 || _known.ContainsKey(key))
            {
                throw new ArgumentException("Discovered keys must be nonempty, bounded and globally ordinal unique.", nameof(source));
            }

            for (string? ancestor = parent; ancestor is not null; ancestor = _known[ancestor].Parent)
            {
                if (!typeof(T).IsValueType && ReferenceEquals(item, _known[ancestor].Item))
                {
                    throw new ArgumentException("The discovered hierarchy contains an object cycle.", nameof(source));
                }
            }

            _known.Add(key, new(item, key, parent, depth, declaration.HasChildren!(item), declaration.ItemEnabled?.Invoke(item) ?? true, declaration.ItemVisible?.Invoke(item) ?? true));
            keys[index] = key;
        }

        return keys;
    }

    internal sealed record Node(T Item, string Key, string? Parent, int Depth, bool CanExpand, bool Enabled, bool Visible);
}
