namespace Lumyte.Resources;

public sealed class ThreadPoolResourceDispatcher : IResourceDispatcher
{
    public async ValueTask<T> InvokeAsync<T>(
        ResourceExecutionLane lane,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return await Task.Run(
                async () => await operation(cancellationToken).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask InvokeAsync(
        ResourceExecutionLane lane,
        Func<ValueTask> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await Task.Run(
                async () => await operation().ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
