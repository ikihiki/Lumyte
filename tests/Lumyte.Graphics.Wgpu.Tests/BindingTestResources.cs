namespace Lumyte.Graphics.Tests;

internal sealed class BindingTestResources : IDisposable
{
    private readonly List<IDisposable> _resources = [];

    public void Dispose()
    {
        for (int i = _resources.Count - 1; i >= 0; i--)
        {
            _resources[i].Dispose();
        }
    }

    internal T Own<T>(T resource)
        where T : IDisposable
    {
        _resources.Add(resource);
        return resource;
    }
}
