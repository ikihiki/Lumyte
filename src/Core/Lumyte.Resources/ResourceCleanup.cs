namespace Lumyte.Resources;

internal sealed class ResourceCleanup(object cleanup, ResourceDependencyLifetime lifetime)
{
    private object? _cleanup = cleanup;

    internal ResourceDependencyLifetime Lifetime { get; } = lifetime;

    internal bool Completed => _cleanup is null;

    internal ValueTask ExecuteAsync()
    {
        if (_cleanup is IDisposable disposable)
        {
            disposable.Dispose();
            return ValueTask.CompletedTask;
        }

        return ((Func<ValueTask>)_cleanup!)();
    }

    internal void Complete() => _cleanup = null;
}
