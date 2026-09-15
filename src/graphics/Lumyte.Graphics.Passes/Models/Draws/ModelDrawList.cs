using System.Numerics;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

public sealed record ModelDrawItem(ModelGeometryData Geometry, ModelMaterialData Material, Matrix4x4 LocalToWorld,
    ModelDeformationData? Deformation = null, ModelDrawRange? Range = null, bool Visible = true);
public sealed record ModelRenderSnapshot(ModelCamera Camera, ModelDrawSnapshot Draws, ModelLighting Lighting);
public readonly record struct ModelDrawHandle(Guid List, long Slot);

/// <summary>Immutable ordered subtree, exposed for separately compiled provider preparation and retention.</summary>
public sealed class ModelDrawNode
{
    internal ModelDrawNode(ModelOrder order, long slot, ModelDrawItem item, ModelDrawNode? left = null, ModelDrawNode? right = null)
    { Order = order; Slot = slot; Item = item; Left = left; Right = right; Count = 1 + (left?.Count ?? 0) + (right?.Count ?? 0); }
    internal ModelOrder Order { get; }
    internal long Slot { get; }
    internal uint Priority { get { uint x = unchecked((uint)Slot + 0x9e3779b9); x = (x ^ (x >> 16)) * 0x85ebca6b; x = (x ^ (x >> 13)) * 0xc2b2ae35; return x ^ (x >> 16); } }
    public ModelDrawItem Item { get; }
    public ModelDrawNode? Left { get; }
    public ModelDrawNode? Right { get; }
    public int Count { get; }
    internal ModelDrawNode With(ModelDrawNode? left, ModelDrawNode? right) => new(Order, Slot, Item, left, right);
}

public sealed class ModelDrawSnapshot
{
    internal ModelDrawSnapshot(ModelDrawNode? root) { Root = root; }
    public ModelDrawNode? Root { get; }
    public int Count => Root?.Count ?? 0;
    public IEnumerable<ModelDrawItem> Items => Enumerate(Root);
    private static IEnumerable<ModelDrawItem> Enumerate(ModelDrawNode? node)
    {
        if (node is null) { yield break; }
        foreach (var item in Enumerate(node.Left)) { yield return item; }
        yield return node.Item;
        foreach (var item in Enumerate(node.Right)) { yield return item; }
    }
    public static ModelDrawSnapshot From(IEnumerable<ModelDrawItem> items)
    { var list = new ModelDrawList(); foreach (var item in items) { list.Add(item); } return list.Snapshot(); }
}

/// <summary>Serially edited persistent draw collection. Old snapshots remain usable after edits.</summary>
public sealed class ModelDrawList
{
    private readonly Guid id = Guid.NewGuid();
    private readonly Dictionary<long, ModelOrder> orders = [];
    private long nextSlot;
    private ModelDrawSnapshot snapshot = new(null);
    public ModelDrawSnapshot Snapshot() => snapshot;
    public ModelDrawHandle Add(ModelDrawItem draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        long slot = checked(++nextSlot);
        ModelOrder order = Last(snapshot.Root) + 1;
        snapshot = new(Insert(snapshot.Root, new(order, slot, draw)));
        orders.Add(slot, order);
        return new(id, slot);
    }
    public void Set(ModelDrawHandle handle, ModelDrawItem draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        ModelOrder order = Order(handle);
        if (Find(snapshot.Root!, order).Item == draw) { return; }
        snapshot = new(Replace(snapshot.Root!, order, draw));
    }
    public void Remove(ModelDrawHandle handle)
    { ModelOrder order = Order(handle); snapshot = new(Remove(snapshot.Root!, order)); orders.Remove(handle.Slot); }
    public void Clear() { orders.Clear(); snapshot = new(null); }
    public void MoveBefore(ModelDrawHandle handle, ModelDrawHandle? before = null)
    {
        ModelOrder old = Order(handle);
        ModelOrder? target = before is { } h ? Order(h) : null;
        if (handle == before) { return; }
        var item = Find(snapshot.Root!, old).Item;
        var root = Remove(snapshot.Root!, old);
        ModelOrder order;
        if (target is null) { order = Last(root) + 1; }
        else
        {
            ModelOrder preceding = target.Value - 1;
            for (var n = root; n is not null;)
            { if (n.Order < target) { preceding = n.Order; n = n.Right; } else { n = n.Left; } }
            order = (preceding + target.Value) / 2;
        }
        snapshot = new(Insert(root, new(order, handle.Slot, item))); orders[handle.Slot] = order;
    }
    private ModelOrder Order(ModelDrawHandle handle)
    { if (handle.List != id || !orders.TryGetValue(handle.Slot, out ModelOrder order)) { throw new ArgumentException("The handle is foreign or removed.", nameof(handle)); } return order; }
    private static ModelOrder Last(ModelDrawNode? node) { if (node is null) { return 0; } while (node.Right is not null) { node = node.Right; } return node.Order; }
    private static ModelDrawNode Find(ModelDrawNode node, ModelOrder order) => node.Order == order ? node : Find(order < node.Order ? node.Left! : node.Right!, order);
    private static ModelDrawNode Replace(ModelDrawNode node, ModelOrder order, ModelDrawItem item) => node.Order == order
        ? new(order, node.Slot, item, node.Left, node.Right)
        : order < node.Order ? node.With(Replace(node.Left!, order, item), node.Right) : node.With(node.Left, Replace(node.Right!, order, item));
    private static ModelDrawNode? Merge(ModelDrawNode? left, ModelDrawNode? right) => left is null ? right : right is null ? left
        : left.Priority < right.Priority ? left.With(left.Left, Merge(left.Right, right)) : right.With(Merge(left, right.Left), right.Right);
    private static ModelDrawNode? Remove(ModelDrawNode node, ModelOrder order) => order == node.Order ? Merge(node.Left, node.Right)
        : order < node.Order ? node.With(Remove(node.Left!, order), node.Right) : node.With(node.Left, Remove(node.Right!, order));
    private static ModelDrawNode Insert(ModelDrawNode? root, ModelDrawNode node)
    {
        if (root is null) { return node; }
        if (node.Order < root.Order)
        {
            var left = Insert(root.Left, node); root = root.With(left, root.Right);
            return left.Priority < root.Priority ? left.With(left.Left, root.With(left.Right, root.Right)) : root;
        }
        var right = Insert(root.Right, node); root = root.With(root.Left, right);
        return right.Priority < root.Priority ? right.With(root.With(root.Left, right.Left), right.Right) : root;
    }
}
