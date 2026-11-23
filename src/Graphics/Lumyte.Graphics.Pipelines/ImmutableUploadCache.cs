using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Lumyte.Graphics.Abstractions;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

// A cache belongs to exactly one compiled pipeline/device/generation. Only completed
// immutable native copies enter lookup. Every preparation has its own use token;
// pending versions and failed cleanup owners stay charged and cannot be evicted.
internal sealed class ImmutableUploadCache(GraphicsImmutableUploadPolicy policy) : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _uploads = new(1, 1);
    private readonly List<Entry> _entries = [];
    private long _bytes;
    private long _created;
    private long _hits;
    private bool _closed;

    internal GraphicsImmutableUploadPolicy Policy => policy;

    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Users != 0)
                {
                    throw new InvalidOperationException("Immutable upload uses must be returned before cache disposal.");
                }

                Remove(_entries[i]);
            }
        }
    }

    internal async ValueTask<IDisposable> EnterUploadAsync(CancellationToken token)
    {
        await _uploads.WaitAsync(token).ConfigureAwait(false);
        return new UploadUse(_uploads);
    }

    internal void Admit<T, TSchema>(int node, ReadOnlySpan<T> data, int chunkElements)
        where T : unmanaged
        where TSchema : ISchema
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            long bytes = 0;
            int chunks = 0;
            var retained = new HashSet<Entry>();
            for (int offset = 0; offset < data.Length; offset = checked(offset + chunkElements))
            {
                int count = Math.Min(chunkElements, data.Length - offset);
                string hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(data.Slice(offset, count))));
                Entry? match = _entries.FirstOrDefault(e => e.Completed && e.Node == node && e.Schema == typeof(TSchema) && e.Descriptor.Matches(TSchema.Descriptor) && e.Hash == hash && e.Count == count);
                if (match is null)
                {
                    bytes = checked(bytes + ((long)count * TSchema.Descriptor.ElementStride));
                    chunks = checked(chunks + 1);
                }
                else
                {
                    retained.Add(match);
                }
            }

            // Do not evict a matching chunk: it is about to become an active input.
            // Evict only unused, non-input owners, still before new native allocation.
            while ((bytes > policy.MaxBytes - _bytes || chunks > policy.MaxChunks - _entries.Count) && _entries.FirstOrDefault(e => e.Users == 0 && !retained.Contains(e)) is { } victim)
            {
                Remove(victim);
            }

            if (bytes > policy.MaxBytes - _bytes || chunks > policy.MaxChunks - _entries.Count)
            {
                throw new InvalidOperationException("Complete immutable upload demand exceeds available finite retention capacity before any native allocation.");
            }
        }
    }

    internal (long Created, long Hits, long Bytes, object[] Owners, bool[] Completed) Snapshot()
    {
        lock (_gate)
        {
            return (_created, _hits, _bytes, _entries.Select(e => (object)e.Buffer).ToArray(), _entries.Select(e => e.Completed).ToArray());
        }
    }

    internal Lease<T>? Find<T, TSchema>(int node, ReadOnlySpan<T> data)
        where T : unmanaged
        where TSchema : ISchema
    {
        int count = data.Length;
        string hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(data)));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            Entry? entry = _entries.FirstOrDefault(e => e.Completed && e.Node == node && e.Schema == typeof(TSchema) && e.Descriptor.Matches(TSchema.Descriptor) && e.Hash == hash && e.Count == count);
            if (entry is null)
            {
                return null;
            }

            entry.Users = checked(entry.Users + 1);
            _hits++;
            return new(this, entry, (IGraphicsBuffer<T>)entry.Buffer);
        }
    }

    internal Lease<T> Create<T, TSchema>(int node, ReadOnlySpan<T> data, IGraphicDevice device)
        where T : unmanaged
        where TSchema : ISchema
    {
        long bytes = checked((long)data.Length * TSchema.Descriptor.ElementStride);
        string hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(data)));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            while ((bytes > policy.MaxBytes - _bytes || _entries.Count >= policy.MaxChunks) && _entries.FirstOrDefault(e => e.Users == 0) is { } victim)
            {
                Remove(victim);
            }

            if (bytes > policy.MaxBytes - _bytes || _entries.Count >= policy.MaxChunks)
            {
                throw new InvalidOperationException("Immutable upload retention budget exceeded before native allocation; active versions remain owned.");
            }

            IGraphicsBuffer<T> buffer = device.CreateBuffer<T>(new() { Count = (ulong)data.Length, Usage = BufferUsage.CopyDestination | BufferUsage.CopySource | BufferUsage.ShaderRead });
            var entry = new Entry(node, typeof(TSchema), TSchema.Descriptor, hash, data.Length, bytes, buffer);
            _entries.Add(entry);
            _created++;
            _bytes = checked(_bytes + bytes);
            return new(this, entry, buffer);
        }
    }

    private void Remove(Entry entry)
    {
        // Disposal may invalidate native storage before throwing. Both matching
        // lookups must stop reusing it, while the owner and charge remain retryable.
        entry.Completed = false;
        entry.Buffer.Dispose(); // Preserve entry and reservation on cleanup failure.
        _entries.Remove(entry);
        _bytes -= entry.Bytes;
    }

    internal sealed class Entry(int node, Type schema, SchemaDescriptor descriptor, string hash, int count, long bytes, IDisposable buffer)
    {
        internal int Node { get; } = node;

        internal Type Schema { get; } = schema;

        internal SchemaDescriptor Descriptor { get; } = descriptor;

        internal string Hash { get; } = hash;

        internal int Count { get; } = count;

        internal long Bytes { get; } = bytes;

        internal IDisposable Buffer { get; } = buffer;

        internal int Users { get; set; } = 1;

        internal bool Completed { get; set; }
    }

    internal sealed class Lease<T>(ImmutableUploadCache owner, Entry entry, IGraphicsBuffer<T> buffer) : IDisposable
        where T : unmanaged
    {
        private bool _returned;

        internal IGraphicsBuffer<T> Buffer { get; } = buffer;

        internal bool Initialized => entry.Completed;

        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_returned)
                {
                    return;
                }

                if (entry.Users == 1 && (!entry.Completed || owner._closed))
                {
                    owner.Remove(entry);
                }

                entry.Users--;
                _returned = true;
            }
        }

        internal void Publish()
        {
            lock (owner._gate)
            {
                ObjectDisposedException.ThrowIf(_returned, this);
                entry.Completed = true;
            }
        }
    }

    private sealed class UploadUse(SemaphoreSlim gate) : IDisposable
    {
        private bool _returned;

        public void Dispose()
        {
            if (!_returned)
            {
                gate.Release();
                _returned = true;
            }
        }
    }
}
