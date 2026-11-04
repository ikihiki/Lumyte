using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class SolidRectangleResources2D : IDisposable
{
    private const ulong IdleBudget = 8 * 1024 * 1024;
    private readonly IGraphicDevice _device;
    private readonly Stack<IDisposable> _owned = [];
    private readonly List<Entry> _idle = [];
    private readonly object _gate = new();
    private ulong _idleBytes;
    private bool _disposed;

    internal SolidRectangleResources2D(IGraphicDevice device)
    {
        _device = device;
        try
        {
            IGraphicsShader vertex = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(SolidRectangleResources2D).Assembly, "Lumyte.Drawing2D.rectangles-vertex.lshader")));
            IGraphicsShader fragment = Own(device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(SolidRectangleResources2D).Assembly, "Lumyte.Drawing2D.rectangles-fragment.lshader")));
            Pipeline = Own(device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment }));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal IGraphicsPipeline Pipeline { get; }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            while (_idle.Count != 0)
            {
                _idle[^1].Dispose();
                _idleBytes -= _idle[^1].Bytes;
                _idle.RemoveAt(_idle.Count - 1);
            }

            while (_owned.TryPeek(out IDisposable? resource))
            {
                resource.Dispose();
                _owned.Pop();
            }
        }
    }

    internal Entry Rent(int vectors)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_device.Status != GraphicsDeviceStatus.Ready)
            {
                throw new InvalidOperationException("The rectangle pool belongs to an unusable device.");
            }

            for (int i = 0; i < _idle.Count; i++)
            {
                Entry entry = _idle[i];
                if (entry.Capacity >= (ulong)vectors && entry.Capacity <= (ulong)vectors * 2)
                {
                    _idle.RemoveAt(i);
                    _idleBytes -= entry.Bytes;
                    return entry;
                }
            }

            ulong capacity = 64;
            while (capacity < (ulong)vectors)
            {
                capacity = checked(capacity * 2);
            }

            return new(_device, capacity);
        }
    }

    internal void Return(Entry entry)
    {
        lock (_gate)
        {
            if (!entry.Discard && !_disposed && _device.Status == GraphicsDeviceStatus.Ready
                && _idle.Count < 3 && entry.Bytes <= IdleBudget - _idleBytes)
            {
                _idle.Add(entry);
                _idleBytes += entry.Bytes;
                return;
            }

            entry.Discard = true;
            entry.Dispose();
        }
    }

    private T Own<T>(T resource)
        where T : IDisposable
    {
        _owned.Push(resource);
        return resource;
    }

    internal sealed class Entry : IDisposable
    {
        private readonly Stack<IDisposable> _owned = [];

        internal Entry(IGraphicDevice device, ulong capacity)
        {
            Capacity = capacity;
            try
            {
                Upload = Own(device.CreateBuffer<Vector4>(new() { Count = capacity, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload }));
                Data = Own(device.CreateBuffer<Vector4>(new() { Count = capacity, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead }));
                Table = Own(device.CreateArgumentTable(new() { BufferCapacity = 1 }));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal ulong Capacity { get; }

        internal ulong Bytes => checked(Capacity * 32);

        internal IGraphicsBuffer<Vector4> Upload { get; }

        internal IGraphicsBuffer<Vector4> Data { get; }

        internal IArgumentTable Table { get; }

        internal bool Used { get; set; }

        internal bool Discard { get; set; }

        public void Dispose()
        {
            while (_owned.TryPeek(out IDisposable? resource))
            {
                resource.Dispose();
                _owned.Pop();
            }
        }

        private T Own<T>(T resource)
            where T : IDisposable
        {
            _owned.Push(resource);
            return resource;
        }
    }
}
