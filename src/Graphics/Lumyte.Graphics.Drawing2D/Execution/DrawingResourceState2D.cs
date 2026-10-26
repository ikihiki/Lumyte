using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal sealed class DrawingResourceState2D : IDisposable
{
    private readonly object _gate = new();
    private readonly IGraphicDevice? _device;
    private readonly Action? _validate;
    private DrawingResources2D? _resources;
    private IDisposable? _owner;
    private int _users = 1;
    private bool _returned;
    private bool _closing;

    internal DrawingResourceState2D(IGraphicDevice? device, DrawingResources2D? resources = null, IDisposable? owner = null, Action? validate = null)
    {
        if (resources is not null && !ReferenceEquals(device, resources.Device))
        {
            throw new ArgumentException("Drawing resources belong to a different physical device.", nameof(resources));
        }

        if (resources is null && (owner is not null || validate is not null))
        {
            throw new ArgumentException("An external owner or validator requires explicit drawing resources.", nameof(resources));
        }

        (_device, _resources, _owner, _validate) = (device, resources, owner, validate);
    }

    internal DrawingResources2D Resources
    {
        get
        {
            lock (_gate)
            {
                EnsureResources();
                Validate();
                return _resources!;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_returned)
            {
                return;
            }

            Release();
            _returned = true;
        }
    }

    internal IDisposable Retain()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_returned || _closing, this);
            EnsureResources();
            Validate();
            var use = new Use(this);
            _users++;
            return use;
        }
    }

    internal void Validate()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_users == 0 || _closing, this);
            _validate?.Invoke();
        }
    }

    private void EnsureResources()
    {
        if (_resources is not null)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_returned || _closing, this);
        IGraphicDevice device = _device ?? throw new InvalidOperationException("Drawing requires a physical device.");

        // A failed earlier cleanup must finish before a new set of default resources is constructed.
        _owner?.Dispose();
        var native = new NativeOwner();
        _owner = native;
        try
        {
            IGraphicsShader vertex = native.Add(device.CreateShader(DrawingShaders2D.Vertex));
            IGraphicsShader fragment = native.Add(device.CreateShader(DrawingShaders2D.Fragment));
            IGraphicsPipeline pipeline = native.Add(device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment }));
            _resources = new(device, pipeline);
        }
        catch (Exception error)
        {
            try
            {
                native.Dispose();
                _owner = null;
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(error, cleanup);
            }

            throw;
        }
    }

    private void Release()
    {
        if (_users == 1)
        {
            _closing = true;
            _owner?.Dispose();
            _owner = null;
        }

        _users--;
    }

    private sealed class Use(DrawingResourceState2D state) : IDisposable
    {
        private bool _returned;

        public void Dispose()
        {
            lock (state._gate)
            {
                if (_returned)
                {
                    return;
                }

                state.Release();
                _returned = true;
            }
        }
    }

    private sealed class NativeOwner : IDisposable
    {
        private readonly List<IDisposable> _resources = [];

        public void Dispose()
        {
            while (_resources.Count != 0)
            {
                _resources[^1].Dispose();
                _resources.RemoveAt(_resources.Count - 1);
            }
        }

        internal T Add<T>(T resource)
            where T : IDisposable
        {
            _resources.Add(resource);
            return resource;
        }
    }
}
