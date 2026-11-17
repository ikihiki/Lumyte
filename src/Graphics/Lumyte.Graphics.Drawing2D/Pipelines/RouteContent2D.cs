using Lumyte.Composition;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Copies and atomically owns the explicit heterogeneous typed route declarations.</summary>
public sealed class RouteContent2D : ICompositionContent<CommandRoute2D>
{
    private CommandRoute2D[]? _items;

    /// <inheritdoc/>
    public void Initialize(CommandRoute2D[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (_items is not null)
        {
            throw new InvalidOperationException("Content is already initialized or frozen.");
        }

        var copy = (CommandRoute2D[])content.Clone();
        var unique = new HashSet<CommandRoute2D>(ReferenceEqualityComparer.Instance);
        foreach (CommandRoute2D route in copy)
        {
            if (route is null || route.Parent is not null || !unique.Add(route))
            {
                throw new ArgumentException("Routes must be non-null, distinct and unowned.", nameof(content));
            }
        }

        foreach (CommandRoute2D route in copy)
        {
            route.Parent = this;
        }

        _items = copy;
    }

    internal CommandRoute2D[] Snapshot() => _items is null
        ? throw new InvalidOperationException("Explicit route content is required.")
        : (CommandRoute2D[])_items.Clone();
}
