using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A mutable ordered logical tree with immutable, reusable drawing content.</summary>
/// <remarks>
/// Operations serialize under a lock; a live borrow rejects mutations. Own content precedes children;
/// interleaving is represented by ordered content child nodes. Topology edits rebuild the placement plan
/// on the next read in O(nodes); property edits resolve only their subtree. Unchanged borrows are O(1).
/// No command replay, payload copying, or GPU ownership transfer occurs during scene edits.
/// </remarks>
public sealed class DrawingScene2D : IDisposable
{
    private readonly object _gate = new();
    private readonly object _owner = new();
    private readonly List<Node?> _nodes = [];
    private readonly List<ulong> _generations = [];
    private readonly Stack<int> _free = new();
    private DrawingSceneEntry2D[] _entries = [];
    private IReadOnlyList<DrawingSceneEntry2D> _view = Array.AsReadOnly(Array.Empty<DrawingSceneEntry2D>());
    private ulong _revision;
    private ulong _topologyRevision;
    private int _borrows;
    private bool _planDirty = true;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="DrawingScene2D"/> class.Creates an empty scene with a permanent root group.</summary>
    public DrawingScene2D()
    {
        _generations.Add(1);
        Root = new(_owner, 0, 1);
        _nodes.Add(new(Root, null, null));
    }

    /// <summary>Gets the permanent root identity. The root cannot be moved or removed.</summary>
    public DrawingNodeId2D Root { get; }

    internal ulong TopologyRevision => _topologyRevision;

    /// <summary>Adds an empty group to the parent's ordered child sequence.</summary>
    /// <param name="parent">The parent identity.</param>
    /// <param name="index">The insertion index, from zero through the child count.</param>
    /// <returns>The new group identity.</returns>
    public DrawingNodeId2D AddGroup(DrawingNodeId2D parent, int index) => Add(parent, index, null);

    /// <summary>Inserts a placement of immutable content.</summary>
    /// <param name="parent">The parent identity.</param>
    /// <param name="index">The insertion index.</param>
    /// <param name="content">The shared immutable content.</param>
    /// <returns>The new placement identity.</returns>
    public DrawingNodeId2D Insert(DrawingNodeId2D parent, int index, DrawList2D content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Add(parent, index, content);
    }

    /// <summary>Replaces own content without modifying the ordered children.</summary>
    /// <param name="node">The placement identity.</param>
    /// <param name="content">The shared immutable replacement.</param>
    public void ReplaceContent(DrawingNodeId2D node, DrawList2D content)
    {
        ArgumentNullException.ThrowIfNull(content);
        lock (_gate)
        {
            EnsureWritable();
            Node target = Resolve(node);
            if (ReferenceEquals(target.Content, content))
            {
                return;
            }

            bool added = target.Content is null;
            target.Content = content;
            target.ContentRevision = ++_revision;
            if (added)
            {
                _planDirty = true;
            }
            else if (!_planDirty)
            {
                _entries[target.EntryIndex] = Entry(target);
            }
        }
    }

    /// <summary>Sets the local-to-parent transform atomically after validating the affected world transforms.</summary>
    /// <param name="node">The placement identity.</param>
    /// <param name="transform">A finite transform; clipped world transforms must also be invertible.</param>
    public void SetTransform(DrawingNodeId2D node, Matrix3x2 transform)
    {
        lock (_gate)
        {
            EnsureWritable();
            Node target = Resolve(node);
            ModelValidation2D.Matrix(transform, nameof(transform));
            if (target.Local == transform)
            {
                return;
            }

            Matrix3x2 parentWorld = World(target.Parent);
            ValidateSubtree(target, transform * parentWorld, target.Clip, nameof(transform));
            target.Local = transform;
            ulong revision = ++_revision;
            UpdateProperties(target, parentWorld, ParentClips(target), revision);
        }
    }

    /// <summary>Sets a local clip, captured at this node's world transform rather than any child's transform.</summary>
    /// <param name="node">The placement identity.</param>
    /// <param name="clip">The immutable path, or null to remove the clip.</param>
    /// <param name="rule">The clip fill rule.</param>
    public void SetClip(DrawingNodeId2D node, Path2D? clip, FillRule2D rule = FillRule2D.NonZero)
    {
        lock (_gate)
        {
            EnsureWritable();
            Node target = Resolve(node);
            ModelValidation2D.EnumValue(rule, nameof(rule));
            if (ReferenceEquals(target.Clip, clip) && (clip is null || target.Rule == rule))
            {
                return;
            }

            ValidateSubtree(target, World(target), clip, nameof(clip));
            target.Clip = clip;
            target.Rule = rule;
            ulong revision = ++_revision;
            UpdateProperties(target, World(target.Parent), ParentClips(target), revision);
        }
    }

    /// <summary>Moves a subtree. Within one parent, index addresses the final child sequence after removal.</summary>
    /// <param name="node">The non-root placement identity.</param>
    /// <param name="parent">The destination parent, outside the moved subtree.</param>
    /// <param name="index">The final insertion index after removing the node from its old parent.</param>
    public void Move(DrawingNodeId2D node, DrawingNodeId2D parent, int index)
    {
        lock (_gate)
        {
            EnsureWritable();
            Node target = ResolveNonRoot(node);
            Node destination = Resolve(parent);
            for (Node? ancestor = destination; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, target))
                {
                    throw new ArgumentException("A node cannot be moved into its own subtree.", nameof(parent));
                }
            }

            Node oldParent = target.Parent!;
            bool sameParent = ReferenceEquals(oldParent, destination);
            ValidateIndex(index, destination.Children.Count - (sameParent ? 1 : 0));
            int previous = oldParent.Children.IndexOf(target);
            if (sameParent && previous == index)
            {
                return;
            }

            Matrix3x2 parentWorld = World(destination);
            ValidateSubtree(target, target.Local * parentWorld, target.Clip, nameof(parent));
            oldParent.Children.RemoveAt(previous);
            destination.Children.Insert(index, target);
            target.Parent = destination;
            _topologyRevision = ++_revision;
            _planDirty = true;
            if (!sameParent)
            {
                UpdateProperties(target, parentWorld, Clips(destination), _revision);
            }
        }
    }

    /// <summary>Removes a non-root subtree and invalidates all its identities.</summary>
    /// <param name="node">The subtree root identity.</param>
    public void Remove(DrawingNodeId2D node)
    {
        lock (_gate)
        {
            EnsureWritable();
            Node target = ResolveNonRoot(node);
            target.Parent!.Children.Remove(target);
            var pending = new Stack<Node>();
            pending.Push(target);
            while (pending.TryPop(out Node? current))
            {
                foreach (Node child in current.Children)
                {
                    pending.Push(child);
                }

                int slot = current.Id.Slot;
                _nodes[slot] = null;
                if (_generations[slot] != ulong.MaxValue)
                {
                    _generations[slot]++;
                    _free.Push(slot);
                }
            }

            _topologyRevision = ++_revision;
            _planDirty = true;
        }
    }

    /// <summary>Borrows the latest ordered placement plan and prevents all edits until disposal.</summary>
    /// <returns>A read guard that must cover renderer preparation, including awaits.</returns>
    public DrawingSceneReadLease2D Borrow()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsurePlan();
            _borrows++;
            return new(this, _revision, _view);
        }
    }

    /// <summary>Copies placement metadata explicitly; commands and immutable pages remain shared.</summary>
    /// <returns>A frozen plan that survives edits, removals, and scene disposal.</returns>
    public DrawingSnapshot2D CaptureSnapshot()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsurePlan();
            return new(_revision, (DrawingSceneEntry2D[])_entries.Clone());
        }
    }

    /// <summary>Prevents new edits and reads, preserving already borrowed or frozen views.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }
    }

    internal void ReleaseBorrow()
    {
        lock (_gate)
        {
            _borrows--;
        }
    }

    private static void ValidateIndex(int index, int count)
    {
        if (index < 0 || index > count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private static Matrix3x2 World(Node? node)
    {
        Matrix3x2 world = Matrix3x2.Identity;
        for (Node? current = node; current is not null; current = current.Parent)
        {
            world *= current.Local;
        }

        return world;
    }

    private static void ValidateSubtree(Node root, Matrix3x2 world, Path2D? clip, string name)
    {
        var pending = new Stack<(Node Node, Matrix3x2 World, Path2D? Clip)>();
        pending.Push((root, world, clip));
        while (pending.TryPop(out (Node Node, Matrix3x2 World, Path2D? Clip) item))
        {
            ModelValidation2D.Matrix(item.World, name);
            if (item.Clip is not null)
            {
                if (!Matrix3x2.Invert(item.World, out Matrix3x2 inverse))
                {
                    throw new ArgumentException("Clipped world transforms must be invertible.", name);
                }

                ModelValidation2D.Matrix(inverse, name);
            }

            foreach (Node child in item.Node.Children)
            {
                pending.Push((child, child.Local * item.World, child.Clip));
            }
        }
    }

    private static IReadOnlyList<DrawingSceneClip2D> ParentClips(Node node) => node.Parent is null ? Array.Empty<DrawingSceneClip2D>() : Clips(node.Parent);

    private static IReadOnlyList<DrawingSceneClip2D> Clips(Node node)
    {
        var ancestors = new Stack<Node>();
        for (Node? current = node; current is not null; current = current.Parent)
        {
            ancestors.Push(current);
        }

        var clips = new List<DrawingSceneClip2D>();
        Matrix3x2 world = Matrix3x2.Identity;
        while (ancestors.TryPop(out Node? current))
        {
            world = current.Local * world;
            if (current.Clip is not null)
            {
                clips.Add(new(current.Clip, current.Rule, world));
            }
        }

        return Array.AsReadOnly(clips.ToArray());
    }

    private static DrawingSceneEntry2D Entry(Node node) => new(node.Id, node.Content!, node.World, node.WorldClips, node.ContentRevision, node.PropertyRevision);

    private static void UpdatePropertiesForPlan(Node root)
    {
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.TryPop(out Node? node))
        {
            node.World = node.Local * (node.Parent?.World ?? Matrix3x2.Identity);
            IReadOnlyList<DrawingSceneClip2D> inherited = node.Parent?.WorldClips ?? Array.Empty<DrawingSceneClip2D>();
            if (node.Clip is null)
            {
                node.WorldClips = inherited;
            }
            else if (node.WorldClips.Count != inherited.Count + 1 || !node.WorldClips.Take(inherited.Count).SequenceEqual(inherited) || node.WorldClips[^1] != new DrawingSceneClip2D(node.Clip, node.Rule, node.World))
            {
                node.WorldClips = Array.AsReadOnly(inherited.Append(new DrawingSceneClip2D(node.Clip, node.Rule, node.World)).ToArray());
            }

            foreach (Node child in node.Children)
            {
                pending.Push(child);
            }
        }
    }

    private DrawingNodeId2D Add(DrawingNodeId2D parent, int index, DrawList2D? content)
    {
        lock (_gate)
        {
            EnsureWritable();
            Node destination = Resolve(parent);
            ValidateIndex(index, destination.Children.Count);
            int slot = _free.Count == 0 ? _nodes.Count : _free.Pop();
            if (slot == _nodes.Count)
            {
                _nodes.Add(null);
                _generations.Add(1);
            }

            var id = new DrawingNodeId2D(_owner, slot, _generations[slot]);
            ulong revision = ++_revision;
            var node = new Node(id, destination, content)
            {
                ContentRevision = revision,
                PropertyRevision = revision,
            };
            _nodes[slot] = node;
            destination.Children.Insert(index, node);
            _topologyRevision = revision;
            _planDirty = true;
            return id;
        }
    }

    private Node Resolve(DrawingNodeId2D id)
    {
        if (!ReferenceEquals(id.Owner, _owner) || id.Slot < 0 || id.Slot >= _nodes.Count || _generations[id.Slot] != id.Generation || _nodes[id.Slot] is not Node node)
        {
            throw new ArgumentException("The placement identity is default, foreign, or stale.", nameof(id));
        }

        return node;
    }

    private Node ResolveNonRoot(DrawingNodeId2D id)
    {
        Node node = Resolve(id);
        if (node.Parent is null)
        {
            throw new ArgumentException("The root cannot be moved or removed.", nameof(id));
        }

        return node;
    }

    private void EnsureWritable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_borrows != 0)
        {
            throw new InvalidOperationException("Scene mutations are forbidden while read leases are alive.");
        }
    }

    private void UpdateProperties(Node root, Matrix3x2 parentWorld, IReadOnlyList<DrawingSceneClip2D> parentClips, ulong revision)
    {
        var pending = new Stack<(Node Node, Matrix3x2 ParentWorld, IReadOnlyList<DrawingSceneClip2D> ParentClips)>();
        pending.Push((root, parentWorld, parentClips));
        while (pending.TryPop(out (Node Node, Matrix3x2 ParentWorld, IReadOnlyList<DrawingSceneClip2D> ParentClips) item))
        {
            Node node = item.Node;
            node.World = node.Local * item.ParentWorld;
            node.WorldClips = item.ParentClips;
            if (node.Clip is not null)
            {
                var clips = new DrawingSceneClip2D[item.ParentClips.Count + 1];
                for (int i = 0; i < item.ParentClips.Count; i++)
                {
                    clips[i] = item.ParentClips[i];
                }

                clips[^1] = new(node.Clip, node.Rule, node.World);
                node.WorldClips = Array.AsReadOnly(clips);
            }

            node.PropertyRevision = revision;
            if (!_planDirty && node.Content is not null)
            {
                _entries[node.EntryIndex] = Entry(node);
            }

            foreach (Node child in node.Children)
            {
                pending.Push((child, node.World, node.WorldClips));
            }
        }
    }

    private void EnsurePlan()
    {
        if (!_planDirty)
        {
            return;
        }

        Node root = _nodes[0]!;
        UpdatePropertiesForPlan(root);
        var entries = new List<DrawingSceneEntry2D>();
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.TryPop(out Node? node))
        {
            if (node.Content is not null)
            {
                node.EntryIndex = entries.Count;
                entries.Add(Entry(node));
            }

            for (int i = node.Children.Count - 1; i >= 0; i--)
            {
                pending.Push(node.Children[i]);
            }
        }

        _entries = entries.ToArray();
        _view = Array.AsReadOnly(_entries);
        _planDirty = false;
    }

    private sealed class Node(DrawingNodeId2D id, Node? parent, DrawList2D? content)
    {
        internal DrawingNodeId2D Id { get; } = id;

        internal Node? Parent { get; set; } = parent;

        internal DrawList2D? Content { get; set; } = content;

        internal List<Node> Children { get; } = [];

        internal Matrix3x2 Local { get; set; } = Matrix3x2.Identity;

        internal Path2D? Clip { get; set; }

        internal FillRule2D Rule { get; set; }

        internal Matrix3x2 World { get; set; } = Matrix3x2.Identity;

        internal IReadOnlyList<DrawingSceneClip2D> WorldClips { get; set; } = Array.Empty<DrawingSceneClip2D>();

        internal ulong ContentRevision { get; set; }

        internal ulong PropertyRevision { get; set; }

        internal int EntryIndex { get; set; }
    }
}
