using System.Numerics;

namespace Lumyte.Graphics.TwoD;

/// <summary>A retained, ordered 2D scene with stable node IDs and per-node dirty tracking.</summary>
public sealed class Scene
{
    private readonly List<MutableNode?> nodes = [];
    private readonly Stack<int> freeSlots = [];
    private ulong nextRevision = 1;
    private int nextOrder;

    public int Count { get; private set; }

    public NodeId CreateNode()
    {
        int slot = freeSlots.TryPop(out int reused) ? reused : nodes.Count;
        uint generation = slot < nodes.Count && nodes[slot] is { } old
            ? checked(old.Generation + 1)
            : 1;
        var node = new MutableNode(generation, nextRevision++, nextOrder++);
        if (slot == nodes.Count) { nodes.Add(node); }
        else { nodes[slot] = node; }
        Count++;
        return new(slot, generation);
    }

    public void Remove(NodeId node)
    {
        MutableNode current = Require(node);
        nodes[node.Slot] = new MutableNode(current.Generation, nextRevision++, current.Order)
        {
            Removed = true,
        };
        freeSlots.Push(node.Slot);
        Count--;
    }

    public void SetContent(NodeId node, SceneContent content)
    {
        if (content.Equals(default(SceneContent)))
        {
            throw new ArgumentException("Scene content cannot be empty.", nameof(content));
        }
        MutableNode current = Require(node);
        current.Content = content;
        current.Revision = nextRevision++;
    }

    public void SetTransform(NodeId node, Matrix3x2 transform)
    {
        ValidateTransform(transform);
        MutableNode current = Require(node);
        if (current.Transform == transform) { return; }
        current.Transform = transform;
        current.Revision = nextRevision++;
    }

    public void SetClip(NodeId node, Rect? clip)
    {
        Rect? validated = clip?.Validate();
        MutableNode current = Require(node);
        if (current.Clip == validated) { return; }
        current.Clip = validated;
    }

    public void SetVisible(NodeId node, bool visible)
    {
        MutableNode current = Require(node);
        if (current.Visible == visible) { return; }
        current.Visible = visible;
    }

    public void SetOrder(NodeId node, int order)
    {
        MutableNode current = Require(node);
        if (current.Order == order) { return; }
        current.Order = order;
    }

    internal SceneNodeState[] Capture()
        => nodes
            .Select((node, slot) => (node, slot))
            .Where(static item => item.node is { Removed: false })
            .Select(static item => new SceneNodeState(
                item.slot,
                item.node!.Generation,
                item.node.Revision,
                item.node.Order,
                item.node.Visible,
                item.node.Content,
                item.node.Transform,
                item.node.Clip))
            .OrderBy(static node => node.Order)
            .ThenBy(static node => node.Slot)
            .ToArray();

    internal int SlotCount => nodes.Count;

    private MutableNode Require(NodeId node)
    {
        if (node.IsNull || node.Slot < 0 || node.Slot >= nodes.Count
            || nodes[node.Slot] is not { Removed: false } value
            || value.Generation != node.Generation)
        {
            throw new ArgumentException("Scene node is stale or does not belong to this scene.", nameof(node));
        }
        return value;
    }

    private static void ValidateTransform(Matrix3x2 transform)
    {
        if (!float.IsFinite(transform.M11) || !float.IsFinite(transform.M12)
            || !float.IsFinite(transform.M21) || !float.IsFinite(transform.M22)
            || !float.IsFinite(transform.M31) || !float.IsFinite(transform.M32))
        {
            throw new ArgumentOutOfRangeException(nameof(transform));
        }
    }

    private sealed class MutableNode(uint generation, ulong revision, int order)
    {
        public uint Generation { get; } = generation;
        public ulong Revision { get; set; } = revision;
        public int Order { get; set; } = order;
        public bool Visible { get; set; } = true;
        public SceneContent? Content { get; set; }
        public Matrix3x2 Transform { get; set; } = Matrix3x2.Identity;
        public Rect? Clip { get; set; }
        public bool Removed { get; set; }
    }
}
