using Microsoft.Extensions.ObjectPool;

namespace Lumyte.Graphics.Drawing2D;

// Metadata only: refcounts never grant GPU lifetime or authorize resource recycling.
internal sealed class RetainedOwnerLedger2D
{
    private static readonly ObjectPool<Entry> _pool = new DefaultObjectPool<Entry>(new EntryPolicy(), 256);
    private readonly Dictionary<object, Entry> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly List<Entry> _variable = [];
    private Int128 _idleBytes;
    private Int128 _allBytes;

    internal int OwnerCount => _owners.Count;

    internal void Attach(KeyValuePair<object, long>[] owners, bool idle)
    {
        foreach ((object owner, long bytes) in owners)
        {
            if (!_owners.TryGetValue(owner, out Entry? entry))
            {
                entry = _pool.Get();
                entry.Owner = owner;
                entry.Bytes = bytes;
                _owners.Add(owner, entry);
                if (owner is DrawingArgumentPlan2D)
                {
                    _variable.Add(entry);
                }
            }

            if (entry.Active + entry.Idle == 0)
            {
                _allBytes += entry.Bytes;
            }

            if (idle)
            {
                if (entry.Idle++ == 0)
                {
                    _idleBytes += entry.Bytes;
                }
            }
            else
            {
                entry.Active++;
            }
        }
    }

    internal void Detach(KeyValuePair<object, long>[] owners, bool idle)
    {
        foreach ((object owner, _) in owners)
        {
            if (!_owners.TryGetValue(owner, out Entry? present) || (idle ? present.Idle : present.Active) == 0)
            {
                throw new InvalidOperationException("Owner accounting reference underflow.");
            }
        }

        foreach ((object owner, _) in owners)
        {
            Entry entry = _owners[owner];
            if (idle)
            {
                if (--entry.Idle == 0)
                {
                    _idleBytes -= entry.Bytes;
                }
            }
            else
            {
                entry.Active--;
            }

            if (entry.Idle < 0 || entry.Active < 0)
            {
                throw new InvalidOperationException("Owner accounting reference underflow.");
            }

            if (entry.Active + entry.Idle == 0)
            {
                _allBytes -= entry.Bytes;
                _owners.Remove(owner);
                _variable.Remove(entry);
                _pool.Return(entry);
            }
        }

        if (_owners.Count == 0 && _owners.EnsureCapacity(0) > 4096)
        {
            _owners.TrimExcess();
            if (_variable.Capacity > 4096)
            {
                _variable.TrimExcess();
            }
        }
    }

    internal void ReturnToIdle(KeyValuePair<object, long>[] owners)
    {
        foreach ((object owner, _) in owners)
        {
            Entry entry = _owners[owner];
            if (entry.Active == 0)
            {
                throw new InvalidOperationException("Owner accounting reference underflow.");
            }

            entry.Active--;
            if (entry.Idle++ == 0)
            {
                _idleBytes += entry.Bytes;
            }
        }
    }

    internal long Bytes(bool includeActive)
    {
        // Registrations is public and can be externally materialized. Revalidate each
        // distinct variable plan, never its assets/pages, to preserve live estimates.
        for (int index = _variable.Count - 1; index >= 0; index--)
        {
            Entry entry = _variable[index];
            long bytes = ((DrawingArgumentPlan2D)entry.Owner!).EstimatedBytes;
            long delta = bytes - entry.Bytes;
            _allBytes += delta;
            if (entry.Idle != 0)
            {
                _idleBytes += delta;
            }

            entry.Bytes = bytes;
            if (((DrawingArgumentPlan2D)entry.Owner!).MemorySizeIsStable)
            {
                _variable.RemoveAt(index);
            }
        }

        Int128 total = includeActive ? _allBytes : _idleBytes;
        return total > long.MaxValue ? long.MaxValue : (long)total;
    }

    private sealed class Entry
    {
        internal object? Owner { get; set; }

        internal long Bytes { get; set; }

        internal int Active { get; set; }

        internal int Idle { get; set; }
    }

    private sealed class EntryPolicy : PooledObjectPolicy<Entry>
    {
        public override Entry Create() => new();

        public override bool Return(Entry obj)
        {
            obj.Owner = null;
            obj.Bytes = 0;
            obj.Active = 0;
            obj.Idle = 0;
            return true;
        }
    }
}
