using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>A renderer-local, byte- and asset-bounded LRU of CPU shape preparation only.</summary>
/// <remarks>
/// Sparse arrays are privately owned: every returned plan has independent mutable arrays and current
/// commands. Keys never retain recordings, paints, clips, paths or meshes. Geometry plans expose only
/// read-only curve storage and can be shared. EstimatedBytes counts conservative cache-owned storage,
/// not borrowed source objects, returned plans, uploads or GPU residency. Callers must charge their
/// other live preparation resources separately. Calls are serialized, including cold preparation.
/// </remarks>
internal sealed class DrawingCpuShapeCache2D
{
    private readonly object _gate = new();
    private readonly long _maxCachedBytes;
    private readonly int _maxAssets;
    private readonly LinkedList<Entry> _entries = new();
    private long _estimatedBytes;
    private long _sparseBuildCount;
    private long _geometryBuildCount;

    internal DrawingCpuShapeCache2D(long maxCachedBytes, int maxAssets)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCachedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxAssets);
        _maxCachedBytes = maxCachedBytes;
        _maxAssets = maxAssets;
    }

    internal long SparseBuildCount
    {
        get
        {
            lock (_gate)
            {
                return _sparseBuildCount;
            }
        }
    }

    internal long GeometryBuildCount
    {
        get
        {
            lock (_gate)
            {
                return _geometryBuildCount;
            }
        }
    }

    internal long EstimatedBytes
    {
        get
        {
            lock (_gate)
            {
                return _estimatedBytes;
            }
        }
    }

    internal int AssetCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _estimatedBytes = 0;
        }
    }

    internal SparseStripPlan2D GetSparse(DrawList2D content, DrawingTargetInfo2D target, long maxWorkingBytes, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWorkingBytes);
        token.ThrowIfCancellationRequested();

        // Admit command/key scratch before allocating it. 512 per command also covers boxed key
        // values and weak identity handles; keys contain no strong source-resource references.
        long scratch = checked(1024L + (content.Count * 512L));
        Require(scratch, maxWorkingBytes);
        lock (_gate)
        {
            TrimFor(scratch, maxWorkingBytes);
            DrawingCommand2D[] commands = Commands(content);
            ShapeKey[] shapes = commands.Select(Key).ToArray();
            var grid = new Grid(target.Description.Width, target.Description.Height, target.PixelScale, target.PixelOrigin);
            for (LinkedListNode<Entry>? node = _entries.First; node is not null; node = node.Next)
            {
                if (node.Value is not SparseEntry entry || entry.Grid != grid || !entry.Shapes.AsSpan().SequenceEqual(shapes))
                {
                    continue;
                }

                // Do not let a warm hit bypass a smaller preparation budget. WorkingBytes is the
                // original conservative preparation peak; the new command/key scratch is additional.
                Require(checked(scratch + entry.Plan.WorkingBytes + entry.Bytes), maxWorkingBytes);
                TrimFor(checked(scratch + entry.Plan.WorkingBytes), maxWorkingBytes, node);
                Touch(node);
                token.ThrowIfCancellationRequested();
                return new(commands, (SparseStrip2D[])entry.Plan.Strips.Clone(), (ushort[])entry.Plan.BoundaryMasks.Clone(), entry.Plan.WorkingBytes);
            }

            // Keep at most a quarter of the call budget idle during a cold preparation.
            // Multiple resident assets survive ordinary misses; pressure evicts least-recent first.
            TrimFor(Math.Max(scratch, maxWorkingBytes - (maxWorkingBytes / 4)), maxWorkingBytes);
            Require(checked(scratch + _estimatedBytes + 1), maxWorkingBytes);
            SparseStripPlan2D plan = SparseStripBuilder2D.Create(content, target, maxWorkingBytes - scratch - _estimatedBytes, token);
            _sparseBuildCount++;
            long bytes = checked(512L + (shapes.Length * 512L) + (plan.Strips.Length * 32L) + (plan.BoundaryMasks.Length * 2L));
            if (CanStore(bytes) && bytes <= maxWorkingBytes - scratch - plan.WorkingBytes - _estimatedBytes)
            {
                // Cache owns detached shape arrays and deliberately no command or paint owner.
                var cached = new SparseStripPlan2D([], (SparseStrip2D[])plan.Strips.Clone(), (ushort[])plan.BoundaryMasks.Clone(), plan.WorkingBytes);
                Add(new SparseEntry(bytes, grid, shapes, cached));
            }

            return plan;
        }
    }

    internal ComputeGeometryPlan2D GetGeometry(Path2D path, Matrix3x2 transform, FillRule2D rule, StrokeStyle2D? stroke, long maxWorkingBytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWorkingBytes);
        lock (_gate)
        {
            for (LinkedListNode<Entry>? node = _entries.First; node is not null; node = node.Next)
            {
                if (node.Value is not GeometryEntry entry || !entry.Path.Is(path) || entry.Plan.Transform != transform ||
                    entry.Plan.FillRule != rule || entry.Plan.Stroke != stroke)
                {
                    continue;
                }

                entry.Plan.Capacity.Validate(maxWorkingBytes);

                // Capacity includes this curve owner. Only other idle owners consume
                // additional bytes; eviction never invalidates returned immutable plans.
                TrimFor(Math.Max(0, entry.Plan.Capacity.WorkingBytes - entry.Bytes), maxWorkingBytes, node);
                Touch(node);
                return entry.Plan;
            }

            TrimFor(maxWorkingBytes - (maxWorkingBytes / 4), maxWorkingBytes);
            var plan = ComputeGeometryPlan2D.Create(path, transform, rule, stroke, maxWorkingBytes - _estimatedBytes);
            _geometryBuildCount++;
            long bytes = checked(512L + (plan.Curves.Count * 96L));
            if (CanStore(bytes) && bytes <= maxWorkingBytes - plan.Capacity.WorkingBytes - _estimatedBytes)
            {
                Add(new GeometryEntry(bytes, new(path), plan));
            }

            return plan;
        }
    }

    private static void Require(long bytes, long limit)
    {
        if (bytes > limit)
        {
            throw new NotSupportedException("CPU shape cache preparation exceeds the working-byte budget.");
        }
    }

    private static DrawingCommand2D[] Commands(DrawList2D content) => content.HasRetainedPages
        ? content.RetainedPages.Pages.SelectMany(page => page.Commands).ToArray()
        : content.Commands.ToArray();

    // Paint, source texture coordinates/resources and captured clips are rebound via Commands.
    // SparseStripBuilder2D uses only these shape inputs and Transform, never clip or paint.
    private static ShapeKey Key(DrawingCommand2D command) => command switch
    {
        FillRectCommand2D c => new(1, c.State.Transform, c.Rect),
        FillRoundedRectCommand2D c => new(2, c.State.Transform, c.Rect, Radius: c.Radius),
        FillCircleCommand2D c => new(3, c.State.Transform, A: c.Center, Radius: c.Radius),
        DrawLineCommand2D c => new(4, c.State.Transform, A: c.Start, B: c.End, Stroke: c.Style),
        FillPathCommand2D c => new(5, c.State.Transform, Identity: new(c.Path), Rule: c.Rule),
        StrokePathCommand2D c => new(6, c.State.Transform, Identity: new(c.Path), Stroke: c.Style),
        DrawImageCommand2D c => new(7, c.State.Transform, c.Destination),
        DrawCoverageMaskCommand2D c => new(8, c.State.Transform, c.Destination),
        DrawDistanceFieldCommand2D c => new(9, c.State.Transform, c.Destination),
        DrawMeshCommand2D c => new(10, c.State.Transform, Identity: new(c.Mesh)),
        DrawPaintGraphCommand2D c => new(11, c.State.Transform),
        _ => throw new NotSupportedException("The CPU shape cache does not support this command type."),
    };

    private bool CanStore(long bytes) => _maxAssets > 0 && bytes <= _maxCachedBytes;

    private void Add(Entry entry)
    {
        while (_entries.Count >= _maxAssets || _estimatedBytes > _maxCachedBytes - entry.Bytes)
        {
            Remove(_entries.Last!);
        }

        _entries.AddFirst(entry);
        _estimatedBytes = checked(_estimatedBytes + entry.Bytes);
    }

    private void TrimFor(long working, long limit, LinkedListNode<Entry>? preserve = null)
    {
        Require(working, limit);
        while (_estimatedBytes > limit - working)
        {
            LinkedListNode<Entry>? victim = _entries.Last;
            if (victim == preserve)
            {
                victim = victim?.Previous;
            }

            if (victim is null)
            {
                // A hit can be served after removing its idle owner: the local entry remains alive.
                Remove(preserve!);
                break;
            }

            Remove(victim);
        }
    }

    private void Touch(LinkedListNode<Entry> node)
    {
        if (node.List is not null)
        {
            _entries.Remove(node);
            _entries.AddFirst(node);
        }
    }

    private void Remove(LinkedListNode<Entry> node)
    {
        _estimatedBytes -= node.Value.Bytes;
        _entries.Remove(node);
    }

    private readonly record struct Grid(uint Width, uint Height, float Scale, Vector2 Origin);

    private readonly record struct ShapeKey(int Kind, Matrix3x2 Transform, Rect2D Rect = default,
        Vector2 A = default, Vector2 B = default, float Radius = 0, Identity Identity = default,
        FillRule2D Rule = FillRule2D.NonZero, StrokeStyle2D? Stroke = null);

    private readonly struct Identity : IEquatable<Identity>
    {
        private readonly WeakReference<object>? _reference;

        internal Identity(object value)
        {
            _reference = new(value);
        }

        public bool Equals(Identity other) => _reference is null ? other._reference is null :
            _reference.TryGetTarget(out object? target) && other.Is(target);

        public override bool Equals(object? obj) => obj is Identity other && Equals(other);

        // Equality is checked only in bounded linear LRU scans, never in a hash table.
        public override int GetHashCode() => 0;

        internal bool Is(object value) => _reference is not null && _reference.TryGetTarget(out object? target) && ReferenceEquals(target, value);
    }

    private abstract record Entry(long Bytes);

    private sealed record SparseEntry(long Bytes, Grid Grid, ShapeKey[] Shapes, SparseStripPlan2D Plan) : Entry(Bytes);

    private sealed record GeometryEntry(long Bytes, Identity Path, ComputeGeometryPlan2D Plan) : Entry(Bytes);
}
