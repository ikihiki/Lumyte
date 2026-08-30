namespace Lumyte.Resources;

public sealed class SynchronizationContextResourceDispatcher
    : IResourceDispatcher
{
    private readonly SynchronizationContext context;

    public SynchronizationContextResourceDispatcher(SynchronizationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
    }

    public ValueTask<T> InvokeAsync<T>(
        ResourceExecutionLane lane,
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(
            async _ =>
            {
                try
                {
                    completion.TrySetResult(
                        await operation(cancellationToken).ConfigureAwait(true));
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            state: null);
        return new ValueTask<T>(completion.Task);
    }

    public ValueTask InvokeAsync(
        ResourceExecutionLane lane,
        Func<ValueTask> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(
            async _ =>
            {
                try
                {
                    await operation().ConfigureAwait(true);
                    completion.TrySetResult();
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            },
            state: null);
        return new ValueTask(completion.Task);
    }
}
