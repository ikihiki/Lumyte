using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

internal sealed class BackendCompileScope2D(IGraphicDevice device) : IDisposable
{
    private readonly Dictionary<object, object> _instances = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, object> _names = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _owned = [];

    public void Dispose()
    {
        while (_owned.Count != 0)
        {
            int last = _owned.Count - 1;
            _owned[last].Dispose();
            _owned.RemoveAt(last);
        }
    }

    internal T Resolve<T>(SharedBackend2D<T> shared)
        where T : class, IDisposable
    {
        if (_names.TryGetValue(shared.Name, out object? existing) && !ReferenceEquals(existing, shared))
        {
            throw new ArgumentException("Conflicting declarations for the same shared backend identity.");
        }

        _names[shared.Name] = shared;
        if (_instances.TryGetValue(shared, out object? value))
        {
            return (T)value;
        }

        T result = shared.Create(device) ?? throw new InvalidOperationException("A shared factory returned null.");
        _owned.Add(result);
        _instances.Add(shared, result);
        return result;
    }
}
