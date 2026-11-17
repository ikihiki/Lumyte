using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>A graph-local backend identity. Reuse this declaration, not a type name, to share.</summary>
/// <typeparam name="T">The actual shared backend or manager type.</typeparam>
/// <param name="name">A unique graph-local diagnostic name.</param>
/// <param name="create">Creates one owned instance per compiled renderer.</param>
public sealed class SharedBackend2D<T>(string name, Func<IGraphicDevice, T> create)
    where T : class, IDisposable
{
    internal string Name { get; } = !string.IsNullOrWhiteSpace(name) ? name : throw new ArgumentException("A shared identity needs a name.", nameof(name));

    internal Func<IGraphicDevice, T> Create { get; } = create ?? throw new ArgumentNullException(nameof(create));
}
