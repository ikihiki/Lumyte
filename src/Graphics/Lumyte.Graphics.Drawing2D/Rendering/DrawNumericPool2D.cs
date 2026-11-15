using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class DrawNumericPool2D(IGraphicDevice device, long budget) : IDisposable
{
    private readonly List<Entry> _idle = [];
    private readonly object _gate = new();
    private long _idleBytes;
    private bool _disposed;

    internal long IdleBytes
    {
        get
        {
            lock (_gate)
            {
                return _idleBytes;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Trim(0);
        }
    }

    internal Lease Rent(int count)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (int i = 0; i < _idle.Count; i++)
            {
                Entry entry = _idle[i];
                if (!entry.Discard && entry.Capacity >= (ulong)count && entry.Capacity <= (ulong)count * 2)
                {
                    _idle.RemoveAt(i);
                    _idleBytes -= entry.Bytes;
                    return new(this, entry);
                }
            }

            return new(this, new(device, checked((ulong)count)));
        }
    }

    internal void Trim(long maximum)
    {
        lock (_gate)
        {
            while (_idle.Count != 0 && _idleBytes > maximum)
            {
                Entry entry = _idle[^1];
                entry.Discard = true;
                entry.Dispose();
                _idleBytes -= entry.Bytes;
                _idle.RemoveAt(_idle.Count - 1);
            }
        }
    }

    private void Return(Entry entry)
    {
        lock (_gate)
        {
            if (!_disposed && !entry.Discard && device.Status == GraphicsDeviceStatus.Ready && _idle.Count < 3 && entry.Bytes <= Math.Min(8 * 1024 * 1024, budget) - _idleBytes)
            {
                _idle.Add(entry);
                _idleBytes += entry.Bytes;
                return;
            }

            entry.Discard = true;
            entry.Dispose();
        }
    }

    internal sealed class Lease(DrawNumericPool2D owner, Entry entry) : IDisposable
    {
        private bool _returned;

        internal IGraphicsBuffer<Vector4> Upload => entry.Upload;

        internal IGraphicsBuffer<Vector4> Data => entry.Data;

        public void Dispose()
        {
            if (!_returned)
            {
                owner.Return(entry);
                _returned = true;
            }
        }
    }

    internal sealed class Entry : IDisposable
    {
        private readonly List<IDisposable> _owned = [];

        internal Entry(IGraphicDevice device, ulong capacity)
        {
            Capacity = capacity;
            try
            {
                Upload = device.CreateBuffer<Vector4>(new() { Count = capacity, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
                _owned.Add(Upload);
                Data = device.CreateBuffer<Vector4>(new() { Count = capacity, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
                _owned.Add(Data);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal ulong Capacity { get; }

        internal long Bytes => checked((long)Capacity * 32);

        internal IGraphicsBuffer<Vector4> Upload { get; }

        internal IGraphicsBuffer<Vector4> Data { get; }

        internal bool Discard { get; set; }

        public void Dispose()
        {
            while (_owned.Count != 0)
            {
                _owned[^1].Dispose();
                _owned.RemoveAt(_owned.Count - 1);
            }
        }
    }
}
