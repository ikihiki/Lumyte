using System.Numerics;

namespace Lumyte.Graphics.TwoD;

public readonly struct Draw2DNodeId : IEquatable<Draw2DNodeId>
{
    internal Draw2DNodeId(object owner, long serial) { Owner = owner; Serial = serial; }
    internal object? Owner { get; }
    internal long Serial { get; }
    public bool Equals(Draw2DNodeId other) => ReferenceEquals(Owner, other.Owner) && Serial == other.Serial;
    public override bool Equals(object? obj) => obj is Draw2DNodeId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Owner, Serial);
    public static bool operator ==(Draw2DNodeId left, Draw2DNodeId right) => left.Equals(right);
    public static bool operator !=(Draw2DNodeId left, Draw2DNodeId right) => !left.Equals(right);
}

/// <summary>A retained scene with persistent ordered subtrees; changing one node copies only its ancestor chain.</summary>
public sealed class Draw2DSceneStore
{
    private readonly object identity = new();
    private readonly Dictionary<long, Entry> nodes = [];
    private long nextId;
    private Tree? root;
    private Draw2DScene? snapshot;
    private float snapshotScale;
    public int Count => nodes.Count;

    public Draw2DNodeId CreateNode(Draw2DScene content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Draw2DSceneBuilder.RequireLogicalChild(content);
        var id = checked(++nextId);
        var entry = new Entry(id, content, Matrix3x2.Identity, null, true, 0);
        nodes.Add(id, entry);
        root = Tree.Set(root, entry);
        snapshot = null;
        return new(identity, id);
    }
    public void Remove(Draw2DNodeId node)
    {
        var entry = Require(node);
        nodes.Remove(node.Serial);
        root = Tree.Remove(root, entry);
        snapshot = null;
    }
    public void SetContent(Draw2DNodeId node, Draw2DScene content)
    { ArgumentNullException.ThrowIfNull(content); Draw2DSceneBuilder.RequireLogicalChild(content); Change(node, entry => entry with { Content = content }); }
    public void SetTransform(Draw2DNodeId node, Matrix3x2 transform)
    { Draw2DSceneBuilder.ValidateTransform(transform); Change(node, entry => entry with { Transform = transform }); }
    public void SetClip(Draw2DNodeId node, Rect? clip)
    { clip?.Validate(); Change(node, entry => entry with { Clip = clip }); }
    public void SetVisible(Draw2DNodeId node, bool visible) => Change(node, entry => entry with { Visible = visible });
    public void SetOrder(Draw2DNodeId node, int order) => Change(node, entry => entry with { Order = order });
    public Draw2DScene Snapshot(float deviceScale = 1)
    {
        if (!float.IsFinite(deviceScale) || deviceScale <= 0)
        { throw new ArgumentOutOfRangeException(nameof(deviceScale)); }
        if (snapshot is not null && snapshotScale == deviceScale)
        { return snapshot; }
        snapshotScale = deviceScale;
        return snapshot = new(root is null ? [] : [new Draw2DSceneCommand(root.Scene, Draw2DState.Identity)], deviceScale);
    }
    private Entry Require(Draw2DNodeId node)
    {
        if (!ReferenceEquals(node.Owner, identity) || !nodes.TryGetValue(node.Serial, out var entry))
        { throw new ArgumentException("Node does not belong to this store or was removed.", nameof(node)); }
        return entry;
    }
    private void Change(Draw2DNodeId node, Func<Entry, Entry> update)
    {
        var old = Require(node);
        var next = update(old);
        if (old == next)
        { return; }
        if (old.Order != next.Order)
        { root = Tree.Remove(root, old); }
        nodes[node.Serial] = next;
        root = Tree.Set(root, next);
        snapshot = null;
    }
    private sealed record Entry(long Id, Draw2DScene Content, Matrix3x2 Transform, Rect? Clip, bool Visible, int Order)
    {
        public int CompareTo(Entry other) { var result = Order.CompareTo(other.Order); return result != 0 ? result : Id.CompareTo(other.Id); }
    }
    private sealed class Tree
    {
        private Tree(Entry entry, Tree? left, Tree? right)
        {
            Entry = entry;
            Left = left;
            Right = right;
            Height = Math.Max(left?.Height ?? 0, right?.Height ?? 0) + 1;
            var commands = new List<Draw2DCommand>(3);
            if (left is not null)
            { commands.Add(new Draw2DSceneCommand(left.Scene, Draw2DState.Identity)); }
            if (entry.Visible)
            {
                Draw2DClip[] clips = entry.Clip is { } clip ? [new(clip, null, FillRule.NonZero, entry.Transform)] : [];
                commands.Add(new Draw2DSceneCommand(entry.Content, new(entry.Transform, clips)));
            }
            if (right is not null)
            { commands.Add(new Draw2DSceneCommand(right.Scene, Draw2DState.Identity)); }
            Scene = new(commands, 1);
        }
        private Entry Entry { get; }
        private Tree? Left { get; }
        private Tree? Right { get; }
        private int Height { get; }
        public Draw2DScene Scene { get; }
        public static Tree Set(Tree? node, Entry entry)
        {
            if (node is null)
            { return new(entry, null, null); }
            var compare = entry.CompareTo(node.Entry);
            return Balance(compare < 0 ? new(node.Entry, Set(node.Left, entry), node.Right)
                : compare > 0 ? new(node.Entry, node.Left, Set(node.Right, entry)) : new(entry, node.Left, node.Right));
        }
        public static Tree? Remove(Tree? node, Entry entry)
        {
            if (node is null)
            { return null; }
            var compare = entry.CompareTo(node.Entry);
            if (compare < 0)
            { return Balance(new(node.Entry, Remove(node.Left, entry), node.Right)); }
            if (compare > 0)
            { return Balance(new(node.Entry, node.Left, Remove(node.Right, entry))); }
            if (node.Left is null)
            { return node.Right; }
            if (node.Right is null)
            { return node.Left; }
            var first = node.Right;
            while (first.Left is not null)
            { first = first.Left; }
            return Balance(new(first.Entry, node.Left, Remove(node.Right, first.Entry)));
        }
        private static Tree Balance(Tree node)
        {
            var delta = (node.Left?.Height ?? 0) - (node.Right?.Height ?? 0);
            if (delta > 1)
            {
                var left = node.Left!;
                if ((left.Left?.Height ?? 0) < (left.Right?.Height ?? 0))
                { node = new(node.Entry, RotateLeft(left), node.Right); }
                return RotateRight(node);
            }
            if (delta < -1)
            {
                var right = node.Right!;
                if ((right.Right?.Height ?? 0) < (right.Left?.Height ?? 0))
                { node = new(node.Entry, node.Left, RotateRight(right)); }
                return RotateLeft(node);
            }
            return node;
        }
        private static Tree RotateRight(Tree node)
        { var pivot = node.Left!; return new(pivot.Entry, pivot.Left, new(node.Entry, pivot.Right, node.Right)); }
        private static Tree RotateLeft(Tree node)
        { var pivot = node.Right!; return new(pivot.Entry, new(node.Entry, node.Left, pivot.Left), pivot.Right); }
    }
}
