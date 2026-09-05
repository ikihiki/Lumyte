namespace Lumyte.Resources;

public sealed class RoutingResourceDispatcher : IResourceDispatcher
{
    private readonly IReadOnlyDictionary<ResourceExecutionLane, IResourceDispatcher> routes;
    private readonly IResourceDispatcher fallback;

    public RoutingResourceDispatcher(
        IReadOnlyDictionary<ResourceExecutionLane, IResourceDispatcher> routes,
        IResourceDispatcher? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(routes);
        foreach ((ResourceExecutionLane lane, IResourceDispatcher dispatcher) in routes)
        {
            if (string.IsNullOrWhiteSpace(lane.Name))
            {
                throw new ArgumentException("Resource execution lanes require a name.", nameof(routes));
            }

            ArgumentNullException.ThrowIfNull(dispatcher);
        }

        this.routes = new Dictionary<ResourceExecutionLane, IResourceDispatcher>(routes);
        this.fallback = fallback ?? new InlineResourceDispatcher();
    }

    public ValueTask<T> InvokeAsync<T>(
        ResourceExecutionLane lane,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default) =>
        Resolve(lane).InvokeAsync(lane, operation, cancellationToken);

    public ValueTask InvokeAsync(
        ResourceExecutionLane lane,
        Func<ValueTask> operation,
        CancellationToken cancellationToken = default) =>
        Resolve(lane).InvokeAsync(lane, operation, cancellationToken);

    private IResourceDispatcher Resolve(ResourceExecutionLane lane) =>
        routes.TryGetValue(lane, out IResourceDispatcher? dispatcher)
            ? dispatcher
            : fallback;
}
