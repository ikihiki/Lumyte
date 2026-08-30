namespace Lumyte.Resources;

public interface IResourceDispatcher
{
    ValueTask<T> InvokeAsync<T>(
        ResourceExecutionLane lane,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default);

    ValueTask InvokeAsync(
        ResourceExecutionLane lane,
        Func<ValueTask> operation,
        CancellationToken cancellationToken = default);
}
