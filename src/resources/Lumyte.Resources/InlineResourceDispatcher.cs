namespace Lumyte.Resources;

public sealed class InlineResourceDispatcher : IResourceDispatcher
{
    public ValueTask<T> InvokeAsync<T>(
        ResourceExecutionLane lane,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        return operation(cancellationToken);
    }

    public ValueTask InvokeAsync(
        ResourceExecutionLane lane,
        Func<ValueTask> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        return operation();
    }
}
