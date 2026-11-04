using System.Collections.Immutable;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Incremental.Benchmarks;

/// <summary>
/// Benchmark-only persistent ordered list of immutable DrawList2D fragments.
/// Fixed topology only: no insertion, removal, reorder API, stable node identity,
/// property scopes, GPU preparation, or resource leases. Existing borrowed native
/// resource lifetimes remain the caller's responsibility.
/// </summary>
internal sealed class RetainedListPrototype
{
    public const int ChunkSize = 64;
    private readonly ImmutableList<Chunk> _chunks;

    private RetainedListPrototype(ImmutableList<Chunk> chunks, int count)
    {
        _chunks = chunks;
        Count = count;
    }

    public int Count { get; }

    public DrawList2D this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _chunks[index / ChunkSize][index % ChunkSize];
        }
    }

    public static RetainedListPrototype Create(IReadOnlyList<DrawList2D> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        ImmutableList<Chunk>.Builder chunks = ImmutableList.CreateBuilder<Chunk>();
        int count = fragments.Count;
        for (int start = 0; start < count;)
        {
            int length = Math.Min(ChunkSize, count - start);
            var items = new DrawList2D[length];
            for (int i = 0; i < length; i++)
            {
                DrawList2D value = fragments[start + i];
                ArgumentNullException.ThrowIfNull(value);
                items[i] = value;
            }

            chunks.Add(new Chunk(items));
            start += length;
        }

        return new RetainedListPrototype(chunks.ToImmutable(), count);
    }

    /// <summary>
    /// Requires ascending unique in-range indices and non-null payloads. Input lists
    /// must not mutate during a call. Full validation precedes tree edits. Each dirty
    /// chunk is cloned once, and ImmutableList.Builder batches tree edits: writable
    /// paths are reused until ToImmutable freezes them. No full-scene array clone.
    /// Reference-identical replacements (including an empty batch) return this object.
    /// </summary>
    public RetainedListPrototype ReplaceMany(IReadOnlyList<(int Index, DrawList2D Value)> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        int previous = -1;
        for (int i = 0; i < replacements.Count; i++)
        {
            (int Index, DrawList2D Value) replacement = replacements[i];
            if ((uint)replacement.Index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(replacements), "Replacement index is outside the snapshot.");
            }

            if (replacement.Index <= previous)
            {
                throw new ArgumentException("Replacement indices must be strictly ascending and unique.", nameof(replacements));
            }

            ArgumentNullException.ThrowIfNull(replacement.Value);
            previous = replacement.Index;
        }

        ImmutableList<Chunk>.Builder? builder = null;
        int cursor = 0;
        while (cursor < replacements.Count)
        {
            int chunkIndex = replacements[cursor].Index / ChunkSize;
            Chunk original = _chunks[chunkIndex];
            DrawList2D[]? copy = null;
            do
            {
                (int Index, DrawList2D Value) change = replacements[cursor++];
                int local = change.Index % ChunkSize;
                if (!ReferenceEquals(original[local], change.Value))
                {
                    copy ??= original.Copy();
                    copy[local] = change.Value;
                }
            }
            while (cursor < replacements.Count && replacements[cursor].Index / ChunkSize == chunkIndex);

            if (copy is not null)
            {
                builder ??= _chunks.ToBuilder();
                builder[chunkIndex] = new Chunk(copy);
            }
        }

        return builder is null ? this : new RetainedListPrototype(builder.ToImmutable(), Count);
    }

    /// <summary>Visits payloads in painter order without flattening or replay.</summary>
    public void Visit(Action<DrawList2D> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (Chunk chunk in _chunks)
        {
            chunk.Visit(visitor);
        }
    }

    // Test-only observation; mutable chunk storage never escapes.
    internal bool SharesChunkWith(RetainedListPrototype other, int index)
    {
        if ((uint)index >= (uint)Count || (uint)index >= (uint)other.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return ReferenceEquals(_chunks[index / ChunkSize], other._chunks[index / ChunkSize]);
    }

    private sealed class Chunk(DrawList2D[] ownedItems)
    {
        private readonly DrawList2D[] _items = ownedItems;

        public DrawList2D this[int index] => _items[index];

        public DrawList2D[] Copy() => (DrawList2D[])_items.Clone();

        public void Visit(Action<DrawList2D> visitor)
        {
            foreach (DrawList2D item in _items)
            {
                visitor(item);
            }
        }
    }
}
