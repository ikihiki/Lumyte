using System.Runtime.CompilerServices;

namespace Lumyte.Graphics.Drawing2D;

// Opt-in ownership for integration-owned images. Ordinary borrowed Image2D semantics are unchanged.
internal static class ImageResourceRegistry2D
{
    private static readonly ConditionalWeakTable<Image2D, Resource> _resources = new();

    internal static Resource Register(Image2D image)
    {
        var resource = new Resource();
        Register(image, resource);
        return resource;
    }

    // All descriptors borrowing one live allocation must share its admission gate, including old recordings.
    internal static void Register(Image2D image, Resource resource) => _resources.Add(image, resource);

    internal static IDisposable Retain(IEnumerable<DrawList2D> contents)
    {
        var resources = new HashSet<Resource>();
        foreach (DrawList2D content in contents)
        {
            foreach (DrawImageCommand2D command in content.Commands.OfType<DrawImageCommand2D>())
            {
                if (_resources.TryGetValue(command.Image, out Resource? resource))
                {
                    resources.Add(resource);
                }
            }
        }

        return new Lease(resources.ToArray());
    }

    internal sealed class Resource
    {
        private readonly object _gate = new();
        private int _references;
        private bool _writing;
        private bool _retired;

        internal bool IsReferenced
        {
            get
            {
                lock (_gate)
                {
                    return _references != 0;
                }
            }
        }

        internal WriteLease? TryAcquireWrite()
        {
            lock (_gate)
            {
                if (_references != 0 || _writing || _retired)
                {
                    return null;
                }

                _writing = true;
                return new(this);
            }
        }

        internal void Retain()
        {
            lock (_gate)
            {
                if (_writing || _retired)
                {
                    throw new InvalidOperationException("The live image is being updated or its producer has retired the allocation.");
                }

                _references++;
            }
        }

        internal void Release()
        {
            lock (_gate)
            {
                _references--;
            }
        }

        internal sealed class WriteLease(Resource resource) : IDisposable
        {
            private Resource? _resource = resource;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _resource, null) is { } owner)
                {
                    lock (owner._gate)
                    {
                        owner._writing = false;
                    }
                }
            }

            internal void Invalidate()
            {
                Resource owner = _resource ?? throw new ObjectDisposedException(nameof(WriteLease));
                lock (owner._gate)
                {
                    owner._retired = true;
                }
            }
        }
    }

    private sealed class Lease : IDisposable
    {
        private Resource[]? _resources;

        internal Lease(Resource[] resources)
        {
            int retained = 0;
            try
            {
                foreach (Resource resource in resources)
                {
                    resource.Retain();
                    retained++;
                }

                _resources = resources;
            }
            catch
            {
                for (int i = 0; i < retained; i++)
                {
                    resources[i].Release();
                }

                throw;
            }
        }

        public void Dispose()
        {
            foreach (Resource resource in Interlocked.Exchange(ref _resources, null) ?? [])
            {
                resource.Release();
            }
        }
    }
}
