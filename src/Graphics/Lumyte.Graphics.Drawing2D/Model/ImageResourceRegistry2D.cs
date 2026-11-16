using System.Runtime.CompilerServices;

namespace Lumyte.Graphics.Drawing2D;

// Opt-in ownership for integration-owned images. Ordinary borrowed Image2D semantics are unchanged.
internal static class ImageResourceRegistry2D
{
    private static readonly ConditionalWeakTable<Image2D, Resource> _resources = new();

    internal static Resource Register(Image2D image)
    {
        var resource = new Resource();
        _resources.Add(image, resource);
        return resource;
    }

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
        private int _references;

        internal bool IsReferenced => Volatile.Read(ref _references) != 0;

        internal void Retain() => Interlocked.Increment(ref _references);

        internal void Release() => Interlocked.Decrement(ref _references);
    }

    private sealed class Lease : IDisposable
    {
        private Resource[]? _resources;

        internal Lease(Resource[] resources)
        {
            _resources = resources;
            foreach (Resource resource in resources)
            {
                resource.Retain();
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
